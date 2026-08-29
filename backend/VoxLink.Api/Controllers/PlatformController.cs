using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VoxLink.Api.Auditing;
using VoxLink.Api.Auth;
using VoxLink.Api.Billing;
using VoxLink.Api.Data;
using VoxLink.Api.Models;
using VoxLink.Api.Pdf;

namespace VoxLink.Api.Controllers;

public record OnboardClientRequest(
    string CompanyName,
    string Phone,
    string Country,
    string Region,
    string PrimaryContactName,
    string PrimaryContactEmail,
    string? BillingContactName,
    string? BillingContactEmail,
    string AdminContactName,
    string AdminContactEmail);

public record OnboardClientResponse(Guid CompanyId, Guid AdminUserId, string AdminEmail, bool EmailSent, string? ManualLink);

public record SetLicenseRequest(Guid PlanId, DateTimeOffset ExpiresAt);

public record RejectCompanyRequest(string Reason);

public record ProposeRevokeRequest(string? Reason);

public record ProposePlanChangeRequest(
    string NewName, string? NewDescription, decimal NewMonthlyPrice, int NewIncludedMinutes,
    decimal NewLocalRatePerMin, decimal NewInternationalRatePerMin,
    int NewMinUsers, int? NewMaxUsers, bool NewIsCustomQuote);

public record ReviewPlanChangeRequest(string? Note);

[ApiController]
[Authorize(Policy = "PlatformAdmin")]
[Route("api/platform")]
public class PlatformController : ControllerBase
{
    private readonly VoxLinkDbContext _db;
    private readonly PasswordResetService _passwordResetService;
    private readonly InvoiceGenerationService _invoiceGenerationService;
    private readonly BillingOptions _billingOptions;

    public PlatformController(
        VoxLinkDbContext db, PasswordResetService passwordResetService, InvoiceGenerationService invoiceGenerationService,
        IOptions<BillingOptions> billingOptions)
    {
        _db = db;
        _passwordResetService = passwordResetService;
        _invoiceGenerationService = invoiceGenerationService;
        _billingOptions = billingOptions.Value;
    }

    [HttpGet("companies")]
    public async Task<IActionResult> GetCompanies(CancellationToken cancellationToken)
    {
        var companies = await _db.Companies
            .Where(c => !c.IsInternal)
            .GroupJoin(
                _db.Subscriptions.Include(s => s.Plan),
                c => c.Id,
                s => s.CompanyId,
                (c, subs) => new { Company = c, Subscription = subs.OrderByDescending(s => s.CreatedAt).FirstOrDefault() })
            .Select(x => new
            {
                x.Company.Id,
                x.Company.Name,
                x.Company.Status,
                x.Company.AdminContactName,
                x.Company.AdminContactEmail,
                x.Company.CreatedAt,
                SelectedPlanId = x.Company.SelectedPlanId,
                PlanName = x.Subscription != null ? x.Subscription.Plan!.Name : null,
                LicenseExpiresAt = x.Subscription != null ? (DateTimeOffset?)x.Subscription.CurrentPeriodEnd : null,
                MaxUsers = x.Subscription != null ? x.Subscription.Plan!.MaxUsers : (int?)null
            })
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        var selectedPlanIds = companies.Where(c => c.SelectedPlanId is not null).Select(c => c.SelectedPlanId!.Value).ToList();
        var selectedPlans = await _db.Plans
            .Where(p => selectedPlanIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var userCounts = await _db.Users
            .Where(u => u.Status != "suspended")
            .GroupBy(u => u.CompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.CompanyId, g => g.Count, cancellationToken);

        // The "signup invoice" is any invoice not tied to a recurring subscription —
        // it represents the upfront platform-fee payment made before approval.
        var signupPayments = await _db.Database
            .SqlQuery<SignupPaymentRow>($@"
                select i.company_id as company_id, p.status as status
                from invoices i
                join payments p on p.invoice_id = i.id
                where i.subscription_id is null
                order by p.created_at desc")
            .ToListAsync(cancellationToken);
        var signupPaymentByCompany = signupPayments
            .GroupBy(p => p.CompanyId)
            .ToDictionary(g => g.Key, g => g.First().Status);

        var result = companies.Select(c => new
        {
            c.Id,
            c.Name,
            c.Status,
            c.AdminContactName,
            c.AdminContactEmail,
            c.CreatedAt,
            SelectedPlanName = c.SelectedPlanId is not null && selectedPlans.TryGetValue(c.SelectedPlanId.Value, out var name) ? name : null,
            c.PlanName,
            c.LicenseExpiresAt,
            c.MaxUsers,
            CurrentUserCount = userCounts.GetValueOrDefault(c.Id, 0),
            SignupPaymentStatus = signupPaymentByCompany.GetValueOrDefault(c.Id, "none")
        });

        return Ok(result);
    }

    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans(CancellationToken cancellationToken)
    {
        var plans = await _db.Plans
            .Where(p => p.Status == "active")
            .OrderBy(p => p.MinUsers)
            .Select(p => new { p.Id, p.Name, p.Description, p.MonthlyPrice, p.LocalRatePerMin, p.InternationalRatePerMin, p.IncludedMinutes, p.MinUsers, p.MaxUsers, p.IsCustomQuote })
            .ToListAsync(cancellationToken);

        return Ok(plans);
    }

    /// <summary>
    /// Setting or changing a client's license directly changes what they're
    /// billed for and how much service they get — neither an admin nor an
    /// owner may do it unilaterally, same segregation of duties as revoking
    /// one (see ApprovalsController). This only submits the request.
    /// </summary>
    [HttpPost("companies/{companyId:guid}/license-request")]
    public async Task<IActionResult> ProposeLicenseChange(Guid companyId, SetLicenseRequest request, CancellationToken cancellationToken)
    {
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole is not ("admin" or "owner"))
        {
            return Forbid();
        }

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
        if (company is null) return NotFound();

        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);
        if (plan is null) return NotFound(new { message = "Plan not found." });

        var alreadyPending = await _db.LicenseChangeRequests.AnyAsync(
            r => r.CompanyId == companyId && r.Status == "pending", cancellationToken);
        if (alreadyPending)
        {
            return BadRequest(new { message = "A license change request for this company is already pending review." });
        }

        var changeRequest = new LicenseChangeRequest
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ProposedBy = User.GetUserId(),
            ProposedByRole = callerRole,
            PlanId = plan.Id,
            ExpiresAt = request.ExpiresAt,
            ProposedAt = DateTimeOffset.UtcNow,
            Status = "pending"
        };

        _db.LicenseChangeRequests.Add(changeRequest);
        AuditLogService.LogCrossTenant(_db, companyId, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "license_change.proposed", "company", companyId,
            $"Proposed setting {company.Name} to {plan.Name}, expires {request.ExpiresAt:yyyy-MM-dd}");
        await _db.SaveChangesAsync(cancellationToken);

        var approverRole = callerRole == "admin" ? "an owner" : "a manager";
        return Ok(new { message = $"License change request submitted. Awaiting approval from {approverRole}.", changeRequest.Id });
    }

    [HttpGet("license-change-requests")]
    public async Task<IActionResult> GetLicenseChangeRequests(CancellationToken cancellationToken)
    {
        var requests = await _db.LicenseChangeRequests
            .Include(r => r.Company)
            .Include(r => r.Plan)
            .Where(r => r.Status == "pending")
            .OrderByDescending(r => r.ProposedAt)
            .Select(r => new
            {
                r.Id,
                r.CompanyId,
                CompanyName = r.Company!.Name,
                PlanName = r.Plan!.Name,
                r.ExpiresAt,
                r.ProposedByRole,
                r.ProposedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(requests);
    }

    [HttpPost("companies")]
    public async Task<ActionResult<OnboardClientResponse>> OnboardClient(OnboardClientRequest request, CancellationToken cancellationToken)
    {
        var emailInUse = await _db.Users.AnyAsync(u => u.Email == request.AdminContactEmail, cancellationToken);
        if (emailInUse)
        {
            return Conflict(new { message = "A user with that admin email already exists." });
        }

        var now = DateTimeOffset.UtcNow;
        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = request.CompanyName,
            Phone = request.Phone,
            Country = request.Country,
            Region = request.Region,
            Status = "pending",
            PrimaryContactName = request.PrimaryContactName,
            PrimaryContactEmail = request.PrimaryContactEmail,
            BillingContactName = request.BillingContactName,
            BillingContactEmail = request.BillingContactEmail,
            AdminContactName = request.AdminContactName,
            AdminContactEmail = request.AdminContactEmail,
            CreatedAt = now,
            UpdatedAt = now
        };

        var nameParts = SplitName(request.AdminContactName);
        var admin = new User
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            FirstName = nameParts[0],
            LastName = nameParts[1],
            Email = request.AdminContactEmail,
            // Placeholder hash: nobody can log in with this. The real password is set
            // via the emailed "set your password" link, never handled by this server.
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
            Role = "admin",
            Status = "invited",
            PasswordChangedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Companies.Add(company);
        _db.Users.Add(admin);
        await _db.SaveChangesAsync(cancellationToken);

        // Invited immediately (not at approval) so the client can log in, pick a
        // tier, and submit payment before a platform admin ever reviews them.
        var result = await _passwordResetService.IssueAndSendAsync(_db, admin, isNewAccount: true, cancellationToken);

        return Ok(new OnboardClientResponse(company.Id, admin.Id, admin.Email, result.EmailSent, result.EmailSent ? null : result.Link));
    }

    [HttpPost("companies/{companyId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
        if (company is null) return NotFound();

        if (company.Status != "pending")
        {
            return BadRequest(new { message = "Only a pending company can be approved." });
        }

        company.Status = "active";
        company.UpdatedAt = DateTimeOffset.UtcNow;

        // If they already picked a tier during signup, licensing them is automatic.
        // A platform admin can still override via "Set license" afterwards.
        var hasSubscription = await _db.Subscriptions.AnyAsync(s => s.CompanyId == companyId, cancellationToken);
        if (!hasSubscription && company.SelectedPlanId is Guid planId)
        {
            var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
            if (plan is not null)
            {
                var now = DateTimeOffset.UtcNow;
                _db.Subscriptions.Add(new Subscription
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    PlanId = plan.Id,
                    Status = "active",
                    CurrentPeriodStart = now,
                    CurrentPeriodEnd = now.AddMonths(1),
                    // The platform fee for this first period was already
                    // paid upfront via the signup invoice before approval —
                    // the subscription's first real invoice must not charge
                    // it again.
                    CurrentPeriodFeeBilled = await HasPaidSignupInvoiceAsync(companyId, cancellationToken),
                    CreatedAt = now
                });
            }
        }

        AuditLogService.LogCrossTenant(_db, companyId, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "company.approved", "company", companyId,
            $"{company.Name} approved and fully activated");
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = $"{company.Name} approved and fully activated." });
    }

    /// <summary>
    /// The upfront "platform fee" invoice created during signup (before a
    /// subscription exists) is a genuine payment for the first period's flat
    /// fee — a brand-new subscription must know this so its first real
    /// invoice doesn't charge that fee a second time.
    /// </summary>
    private async Task<bool> HasPaidSignupInvoiceAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _db.Invoices.AnyAsync(
            i => i.CompanyId == companyId && i.SubscriptionId == null && i.Status == "paid", cancellationToken);

    [HttpPost("companies/{companyId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid companyId, RejectCompanyRequest request, CancellationToken cancellationToken)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
        if (company is null) return NotFound();

        if (company.Status != "pending")
        {
            return BadRequest(new { message = "Only a pending company can be rejected." });
        }

        company.Status = "rejected";
        company.RejectedReason = request.Reason;
        company.UpdatedAt = DateTimeOffset.UtcNow;
        AuditLogService.LogCrossTenant(_db, companyId, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "company.rejected", "company", companyId,
            $"{company.Name} rejected: {request.Reason}");
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = $"{company.Name} rejected." });
    }

    /// <summary>
    /// Neither an admin nor an owner may revoke a license unilaterally — an
    /// admin's proposal needs an owner's approval, an owner's proposal needs
    /// a manager's approval (see ApprovalsController). This only submits the
    /// request; nothing happens to the company until it's reviewed.
    /// </summary>
    [HttpPost("companies/{companyId:guid}/revoke-request")]
    public async Task<IActionResult> ProposeRevoke(Guid companyId, ProposeRevokeRequest request, CancellationToken cancellationToken)
    {
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole is not ("admin" or "owner"))
        {
            return Forbid();
        }

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
        if (company is null) return NotFound();
        if (company.Status != "active")
        {
            return BadRequest(new { message = "Only an active company's license can be revoked." });
        }

        var alreadyPending = await _db.LicenseRevokeRequests.AnyAsync(
            r => r.CompanyId == companyId && r.Status == "pending", cancellationToken);
        if (alreadyPending)
        {
            return BadRequest(new { message = "A revoke request for this company is already pending review." });
        }

        var revokeRequest = new LicenseRevokeRequest
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ProposedBy = User.GetUserId(),
            ProposedByRole = callerRole,
            Reason = request.Reason,
            ProposedAt = DateTimeOffset.UtcNow,
            Status = "pending"
        };

        _db.LicenseRevokeRequests.Add(revokeRequest);
        AuditLogService.LogCrossTenant(_db, companyId, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "license_revoke.proposed", "company", companyId,
            $"Proposed revoking {company.Name}'s license" + (request.Reason is { Length: > 0 } r ? $": {r}" : ""));
        await _db.SaveChangesAsync(cancellationToken);

        var approverRole = callerRole == "admin" ? "an owner" : "a manager";
        return Ok(new { message = $"Revoke request submitted. Awaiting approval from {approverRole}.", revokeRequest.Id });
    }

    [HttpGet("revoke-requests")]
    public async Task<IActionResult> GetRevokeRequests(CancellationToken cancellationToken)
    {
        var requests = await _db.LicenseRevokeRequests
            .Include(r => r.Company)
            .Where(r => r.Status == "pending")
            .OrderByDescending(r => r.ProposedAt)
            .Select(r => new
            {
                r.Id,
                r.CompanyId,
                CompanyName = r.Company!.Name,
                r.ProposedByRole,
                r.Reason,
                r.ProposedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(requests);
    }

    [HttpPost("companies/{companyId:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
        if (company is null) return NotFound();

        company.Status = "active";
        company.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { company.Id, company.Status });
    }

    [HttpPost("companies/{companyId:guid}/reset-admin-password")]
    public async Task<IActionResult> ResetAdminPassword(Guid companyId, CancellationToken cancellationToken)
    {
        var admin = await _db.Users
            .Where(u => u.CompanyId == companyId && (u.Role == "owner" || u.Role == "admin"))
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (admin is null) return NotFound(new { message = "No admin user found for that company." });

        var result = await _passwordResetService.IssueAndSendAsync(_db, admin, isNewAccount: false, cancellationToken);

        return Ok(new
        {
            message = result.EmailSent
                ? $"A password reset link has been emailed to {admin.Email}."
                : $"Email failed to send to {admin.Email} — copy the link below and send it manually.",
            manualLink = result.EmailSent ? null : result.Link
        });
    }

    [HttpGet("usage")]
    public async Task<IActionResult> GetUsage(CancellationToken cancellationToken)
    {
        // Each call rounds up to the minute individually before summing —
        // matches how usage/invoices bill (two 9-second calls are 2 minutes,
        // not one minute from summing the raw seconds first). Excludes
        // VoxLink's own internal company: it's not a client, and has its own
        // dedicated usage/billing view.
        var callUsage = await _db.Database
            .SqlQuery<CompanyUsageRow>($@"
                select c.id as company_id, c.name as company_name,
                       count(calls.id) as call_count,
                       coalesce(sum(ceil(calls.duration_seconds::numeric / 60)), 0) as total_minutes
                from companies c
                left join calls on calls.company_id = c.id
                where not c.is_internal
                group by c.id, c.name
                order by c.name")
            .ToListAsync(cancellationToken);

        return Ok(callUsage);
    }

    /// <summary>
    /// Every client's signed pay-as-you-go agreement, so a platform admin
    /// can browse and re-download any of them without digging through email.
    /// </summary>
    [HttpGet("agreements")]
    public async Task<IActionResult> GetAgreements(CancellationToken cancellationToken)
    {
        var agreements = await _db.ServiceAgreements
            .Include(a => a.Company)
            .Where(a => a.Company != null && !a.Company.IsInternal)
            .OrderByDescending(a => a.AgreedAt)
            .Select(a => new
            {
                a.Id,
                CompanyName = a.Company!.Name,
                a.AgreedByName,
                a.AgreedByEmail,
                a.AgreedAt,
                a.TermsVersion
            })
            .ToListAsync(cancellationToken);

        return Ok(agreements);
    }

    [HttpGet("agreements/{id:guid}/pdf")]
    public async Task<IActionResult> GetAgreementPdf(Guid id, [FromServices] Storage.SupabaseStorageClient storage, CancellationToken cancellationToken)
    {
        var agreement = await _db.ServiceAgreements.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (agreement is null) return NotFound();

        var url = await storage.GetSignedUrlAsync(agreement.PdfStoragePath, 300, cancellationToken);
        return Ok(new { url });
    }

    /// <summary>
    /// Revenue vs. cost, bucketed by month and by year: a client's calls are
    /// revenue (what VoxLink bills them), VoxLink's own internal team's calls
    /// are pure cost (nothing gets billed back). Priced using each company's
    /// current plan rates — a simplification for periods before a rate
    /// change, same as every other live usage view in the app.
    /// AtRisk flags a period where internal minutes reached or passed client
    /// minutes: VoxLink's own team is calling as much as, or more than,
    /// everyone actually paying for the platform.
    /// </summary>
    [HttpGet("analytics/revenue-cost")]
    public async Task<IActionResult> GetRevenueCostAnalytics(CancellationToken cancellationToken)
    {
        var calls = await _db.Calls
            .Select(c => new { c.CompanyId, c.CreatedAt, c.DestinationNumber, c.DurationSeconds })
            .ToListAsync(cancellationToken);

        var companies = await _db.Companies.ToDictionaryAsync(c => c.Id, cancellationToken);

        var planByCompany = await _db.Subscriptions
            .Include(s => s.Plan)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
        var latestPlanByCompany = planByCompany
            .GroupBy(s => s.CompanyId)
            .ToDictionary(g => g.Key, g => g.First().Plan!);

        var monthly = new Dictionary<(int Year, int Month), PeriodBucket>();
        var yearly = new Dictionary<int, PeriodBucket>();

        foreach (var companyGroup in calls.GroupBy(c => c.CompanyId))
        {
            if (!companies.TryGetValue(companyGroup.Key, out var company)) continue;
            if (!latestPlanByCompany.TryGetValue(companyGroup.Key, out var plan)) continue;

            foreach (var monthGroup in companyGroup.GroupBy(c => (c.CreatedAt.Year, c.CreatedAt.Month)))
            {
                var rows = monthGroup.Select(c => new CallUsageRow(c.DestinationNumber, c.DurationSeconds)).ToList();
                var usage = UsageCalculator.Compute(rows, plan, _billingOptions.LocalCountryCode);
                var minutes = usage.LocalMinutes + usage.InternationalMinutes;

                AddToBucket(monthly, monthGroup.Key, company.IsInternal, minutes, usage.AmountDue);
                AddToBucket(yearly, monthGroup.Key.Year, company.IsInternal, minutes, usage.AmountDue);
            }
        }

        var monthlyRows = monthly
            .OrderBy(kv => kv.Key)
            .Select(kv => ToRow($"{kv.Key.Year:0000}-{kv.Key.Month:00}", kv.Value))
            .ToList();
        var yearlyRows = yearly
            .OrderBy(kv => kv.Key)
            .Select(kv => ToRow($"{kv.Key:0000}", kv.Value))
            .ToList();

        return Ok(new { monthly = monthlyRows, yearly = yearlyRows });
    }

    private static void AddToBucket<TKey>(Dictionary<TKey, PeriodBucket> buckets, TKey key, bool isInternal, decimal minutes, decimal amount)
        where TKey : notnull
    {
        if (!buckets.TryGetValue(key, out var bucket))
        {
            bucket = new PeriodBucket();
            buckets[key] = bucket;
        }

        if (isInternal)
        {
            bucket.InternalMinutes += minutes;
            bucket.InternalCost += amount;
        }
        else
        {
            bucket.ClientMinutes += minutes;
            bucket.ClientRevenue += amount;
        }
    }

    private static PeriodAnalyticsRow ToRow(string label, PeriodBucket bucket) => new(
        label, bucket.ClientMinutes, bucket.InternalMinutes, bucket.ClientRevenue, bucket.InternalCost,
        AtRisk: (bucket.ClientMinutes > 0 || bucket.InternalMinutes > 0) && bucket.InternalMinutes >= bucket.ClientMinutes);

    [HttpGet("payments/pending")]
    public async Task<IActionResult> GetPendingPayments(CancellationToken cancellationToken)
    {
        var payments = await _db.Payments
            .Include(p => p.Company)
            .Where(p => p.Status == "submitted")
            .OrderBy(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id,
                CompanyName = p.Company!.Name,
                p.InvoiceId,
                p.Amount,
                p.ProofFilePath,
                p.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(payments);
    }

    [HttpGet("payments/{paymentId:guid}/proof")]
    public async Task<IActionResult> GetPaymentProof(Guid paymentId, [FromServices] Storage.SupabaseStorageClient storage, CancellationToken cancellationToken)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment?.ProofFilePath is null) return NotFound();

        var url = await storage.GetSignedUrlAsync(payment.ProofFilePath, 300, cancellationToken);
        return Ok(new { url });
    }

    [HttpPost("payments/{paymentId:guid}/verify")]
    public async Task<IActionResult> VerifyPayment(Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null) return NotFound();

        payment.Status = "succeeded";

        if (payment.InvoiceId is not null)
        {
            var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == payment.InvoiceId, cancellationToken);
            if (invoice is not null)
            {
                invoice.AmountPaid += payment.Amount;
                invoice.Status = invoice.AmountPaid >= invoice.AmountDue ? "paid" : "pending";
                if (invoice.Status == "paid") invoice.PaidAt = DateTimeOffset.UtcNow;
            }
        }

        // Routine restore: a company suspended for non-payment gets reinstated
        // automatically once their payment clears — no separate manual step.
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == payment.CompanyId, cancellationToken);
        if (company is not null && company.Status == "suspended")
        {
            company.Status = "active";
            company.UpdatedAt = DateTimeOffset.UtcNow;
        }

        AuditLogService.LogCrossTenant(_db, payment.CompanyId, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "payment.verified", "payment", payment.Id,
            $"Verified a payment of R{payment.Amount:0.00}" + (company?.Name is { } name ? $" from {name}" : ""));
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Payment verified." });
    }

    [HttpGet("plans/change-requests")]
    public async Task<IActionResult> GetPlanChangeRequests(CancellationToken cancellationToken)
    {
        var requests = await _db.PlanChangeRequests
            .Include(r => r.Plan)
            .OrderByDescending(r => r.ProposedAt)
            .Select(r => new
            {
                r.Id,
                CurrentPlanName = r.Plan!.Name,
                r.ProposedBy,
                r.NewName,
                r.NewDescription,
                r.NewMonthlyPrice,
                r.NewIncludedMinutes,
                r.NewLocalRatePerMin,
                r.NewInternationalRatePerMin,
                r.NewMinUsers,
                r.NewMaxUsers,
                r.NewIsCustomQuote,
                r.Status,
                r.ProposedAt,
                r.ReviewedAt,
                r.ReviewNote
            })
            .ToListAsync(cancellationToken);

        return Ok(requests);
    }

    [HttpPost("plans/{planId:guid}/propose-change")]
    public async Task<IActionResult> ProposePlanChange(Guid planId, ProposePlanChangeRequest request, CancellationToken cancellationToken)
    {
        // Segregation of duties: only an admin proposes a price change, never
        // the owner — the owner's role here is strictly to approve/reject
        // someone else's proposal, not to also raise their own.
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole != "admin")
        {
            return Forbid();
        }

        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null) return NotFound();

        var changeRequest = new PlanChangeRequest
        {
            Id = Guid.NewGuid(),
            PlanId = planId,
            ProposedBy = User.GetUserId(),
            ProposedAt = DateTimeOffset.UtcNow,
            NewName = request.NewName,
            NewDescription = request.NewDescription,
            NewMonthlyPrice = request.NewMonthlyPrice,
            NewIncludedMinutes = request.NewIncludedMinutes,
            NewLocalRatePerMin = request.NewLocalRatePerMin,
            NewInternationalRatePerMin = request.NewInternationalRatePerMin,
            NewMinUsers = request.NewMinUsers,
            NewMaxUsers = request.NewMaxUsers,
            NewIsCustomQuote = request.NewIsCustomQuote,
            Status = "pending"
        };

        _db.PlanChangeRequests.Add(changeRequest);
        AuditLogService.Log(_db, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "price_change.proposed", "plan", planId,
            $"Proposed a price change for {plan.Name}: R{request.NewMonthlyPrice}/mo, {request.NewIncludedMinutes} min included");
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Price change proposed. Awaiting business owner approval.", changeRequest.Id });
    }

    [HttpPost("plans/change-requests/{id:guid}/approve")]
    [Authorize(Policy = "BusinessOwner")]
    public async Task<IActionResult> ApprovePlanChange(Guid id, ReviewPlanChangeRequest request, CancellationToken cancellationToken)
    {
        var changeRequest = await _db.PlanChangeRequests.Include(r => r.Plan).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (changeRequest is null) return NotFound();
        if (changeRequest.Status != "pending") return BadRequest(new { message = "This request has already been reviewed." });
        if (changeRequest.ProposedBy == User.GetUserId())
        {
            return BadRequest(new { message = "You cannot approve a price change you proposed yourself — it must be reviewed by a different business owner." });
        }

        var plan = changeRequest.Plan!;
        var wasLargeTier = plan.Name == "Large";

        plan.Name = changeRequest.NewName;
        plan.Description = changeRequest.NewDescription;
        plan.MonthlyPrice = changeRequest.NewMonthlyPrice;
        plan.IncludedMinutes = changeRequest.NewIncludedMinutes;
        plan.LocalRatePerMin = changeRequest.NewLocalRatePerMin;
        plan.InternationalRatePerMin = changeRequest.NewInternationalRatePerMin;
        plan.MinUsers = changeRequest.NewMinUsers;
        plan.MaxUsers = changeRequest.NewMaxUsers;
        plan.IsCustomQuote = changeRequest.NewIsCustomQuote;

        changeRequest.Status = "approved";
        changeRequest.ReviewedBy = User.GetUserId();
        changeRequest.ReviewedAt = DateTimeOffset.UtcNow;
        changeRequest.ReviewNote = request.Note;

        // VoxLink's own internal-usage cost tracking is priced at the Large
        // tier's per-minute rates (the closest proxy to actual carrier cost)
        // — never its platform fee or included minutes, which is why the
        // Internal Usage plan keeps those at zero. Keep the rates in sync
        // whenever the Large tier's rates change, so internal cost stays
        // accurate in real time without a separate manual update.
        if (wasLargeTier)
        {
            var internalPlan = await _db.Plans.FirstOrDefaultAsync(p => p.Name == "Internal Usage", cancellationToken);
            if (internalPlan is not null)
            {
                internalPlan.LocalRatePerMin = plan.LocalRatePerMin;
                internalPlan.InternationalRatePerMin = plan.InternationalRatePerMin;
            }
        }

        AuditLogService.Log(_db, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "price_change.approved", "plan", plan.Id,
            $"Approved a price change for {plan.Name}: R{plan.MonthlyPrice}/mo, {plan.IncludedMinutes} min included");
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = $"Price change applied to {plan.Name}." });
    }

    [HttpPost("plans/change-requests/{id:guid}/reject")]
    [Authorize(Policy = "BusinessOwner")]
    public async Task<IActionResult> RejectPlanChange(Guid id, ReviewPlanChangeRequest request, CancellationToken cancellationToken)
    {
        var changeRequest = await _db.PlanChangeRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (changeRequest is null) return NotFound();
        if (changeRequest.Status != "pending") return BadRequest(new { message = "This request has already been reviewed." });
        if (changeRequest.ProposedBy == User.GetUserId())
        {
            return BadRequest(new { message = "You cannot reject a price change you proposed yourself — it must be reviewed by a different business owner." });
        }

        changeRequest.Status = "rejected";
        changeRequest.ReviewedBy = User.GetUserId();
        changeRequest.ReviewedAt = DateTimeOffset.UtcNow;
        changeRequest.ReviewNote = request.Note;

        AuditLogService.Log(_db, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "price_change.rejected", "plan_change_request", changeRequest.Id,
            $"Rejected a proposed price change ({changeRequest.NewName}): {request.Note}");
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Price change rejected." });
    }

    [HttpPost("billing/run-cycle")]
    public async Task<IActionResult> RunBillingCycle(CancellationToken cancellationToken)
    {
        var generated = await _invoiceGenerationService.RunOnceAsync(cancellationToken);
        return Ok(new { message = $"{generated} invoice(s) generated." });
    }

    /// <summary>
    /// Lets a platform admin preview, then commit, an ad-hoc invoice for any
    /// chosen client company — the "pick a client, preview, complete and
    /// send or cancel" flow, mirroring a client's own self-service generate
    /// but for any company instead of just the caller's own.
    /// </summary>
    [HttpGet("companies/{companyId:guid}/invoices/preview")]
    public async Task<IActionResult> PreviewClientInvoice(Guid companyId, CancellationToken cancellationToken)
    {
        try
        {
            var preview = await _invoiceGenerationService.PreviewAdHocInvoiceAsync(companyId, cancellationToken);
            return Ok(preview);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// A manually-generated invoice (outside the automatic monthly cycle)
    /// now always needs a manager's approval — this only submits the
    /// request; nothing is actually generated until it's reviewed (see
    /// ApprovalsController). Previews first so an obviously-invalid request
    /// (nothing to bill yet) is rejected immediately rather than left
    /// pending for a manager to discover only at approval time.
    /// </summary>
    [HttpPost("companies/{companyId:guid}/invoices/generate-request")]
    public async Task<IActionResult> ProposeClientInvoiceGeneration(Guid companyId, CancellationToken cancellationToken)
    {
        var alreadyPending = await _db.InvoiceGenerationRequests.AnyAsync(
            r => r.CompanyId == companyId && r.Status == "pending", cancellationToken);
        if (alreadyPending)
        {
            return BadRequest(new { message = "An invoice generation request for this company is already pending review." });
        }

        try
        {
            await _invoiceGenerationService.PreviewAdHocInvoiceAsync(companyId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var generationRequest = new InvoiceGenerationRequest
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ProposedBy = User.GetUserId(),
            ProposedAt = DateTimeOffset.UtcNow,
            Status = "pending"
        };

        _db.InvoiceGenerationRequests.Add(generationRequest);
        AuditLogService.LogCrossTenant(_db, companyId, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "invoice.generation_proposed", "company", companyId,
            "Requested a manually-generated invoice");
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Invoice generation request submitted. Awaiting a manager's approval.", generationRequest.Id });
    }

    [HttpGet("invoice-generation-requests")]
    public async Task<IActionResult> GetInvoiceGenerationRequests(CancellationToken cancellationToken)
    {
        var requests = await _db.InvoiceGenerationRequests
            .Include(r => r.Company)
            .Where(r => r.Status == "pending")
            .OrderByDescending(r => r.ProposedAt)
            .Select(r => new { r.Id, r.CompanyId, CompanyName = r.Company!.Name, r.ProposedAt })
            .ToListAsync(cancellationToken);

        return Ok(requests);
    }

    private static string[] SplitName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', 2);
        return parts.Length == 2 ? parts : [parts[0], ""];
    }
}

public class CompanyUsageRow
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = "";
    public int CallCount { get; set; }
    public decimal TotalMinutes { get; set; }
}

public class PeriodBucket
{
    public decimal ClientMinutes { get; set; }
    public decimal InternalMinutes { get; set; }
    public decimal ClientRevenue { get; set; }
    public decimal InternalCost { get; set; }
}

public record PeriodAnalyticsRow(
    string Label, decimal ClientMinutes, decimal InternalMinutes, decimal ClientRevenue, decimal InternalCost, bool AtRisk);

public class SignupPaymentRow
{
    public Guid CompanyId { get; set; }
    public string Status { get; set; } = "";
}
