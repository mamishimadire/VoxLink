using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxLink.Api.Auth;
using VoxLink.Api.Billing;
using VoxLink.Api.Data;
using VoxLink.Api.Models;

namespace VoxLink.Api.Controllers;

public record OnboardClientRequest(
    string CompanyName,
    string PrimaryContactName,
    string PrimaryContactEmail,
    string? BillingContactName,
    string? BillingContactEmail,
    string AdminContactName,
    string AdminContactEmail);

public record OnboardClientResponse(Guid CompanyId, Guid AdminUserId, string AdminEmail, bool EmailSent, string? ManualLink);

public record SetLicenseRequest(Guid PlanId, DateTimeOffset ExpiresAt);

public record RejectCompanyRequest(string Reason);

public record ProposePlanChangeRequest(
    string NewName, string? NewDescription, decimal NewMonthlyPrice, decimal NewLocalRatePerMin, decimal NewInternationalRatePerMin,
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

    public PlatformController(VoxLinkDbContext db, PasswordResetService passwordResetService, InvoiceGenerationService invoiceGenerationService)
    {
        _db = db;
        _passwordResetService = passwordResetService;
        _invoiceGenerationService = invoiceGenerationService;
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

    [HttpPost("companies/{companyId:guid}/license")]
    public async Task<IActionResult> SetLicense(Guid companyId, SetLicenseRequest request, CancellationToken cancellationToken)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
        if (company is null) return NotFound();

        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);
        if (plan is null) return NotFound(new { message = "Plan not found." });

        var now = DateTimeOffset.UtcNow;
        var subscription = await _db.Subscriptions
            .Where(s => s.CompanyId == companyId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            subscription = new Subscription
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                PlanId = plan.Id,
                Status = "active",
                CurrentPeriodStart = now,
                CurrentPeriodEnd = request.ExpiresAt,
                CreatedAt = now
            };
            _db.Subscriptions.Add(subscription);
        }
        else
        {
            subscription.PlanId = plan.Id;
            subscription.Status = "active";
            subscription.CurrentPeriodStart = now;
            subscription.CurrentPeriodEnd = request.ExpiresAt;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = $"{plan.Name} license set for {company.Name}, expires {request.ExpiresAt:yyyy-MM-dd}." });
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
                    CreatedAt = now
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = $"{company.Name} approved and fully activated." });
    }

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
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = $"{company.Name} rejected." });
    }

    [HttpPost("companies/{companyId:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
        if (company is null) return NotFound();

        company.Status = "suspended";
        company.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { company.Id, company.Status });
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
        var callUsage = await _db.Database
            .SqlQuery<CompanyUsageRow>($@"
                select c.id as company_id, c.name as company_name,
                       count(calls.id) as call_count,
                       coalesce(sum(calls.duration_seconds), 0) as total_duration_seconds
                from companies c
                left join calls on calls.company_id = c.id
                group by c.id, c.name
                order by c.name")
            .ToListAsync(cancellationToken);

        return Ok(callUsage);
    }

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
                r.NewName,
                r.NewDescription,
                r.NewMonthlyPrice,
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
            NewLocalRatePerMin = request.NewLocalRatePerMin,
            NewInternationalRatePerMin = request.NewInternationalRatePerMin,
            NewMinUsers = request.NewMinUsers,
            NewMaxUsers = request.NewMaxUsers,
            NewIsCustomQuote = request.NewIsCustomQuote,
            Status = "pending"
        };

        _db.PlanChangeRequests.Add(changeRequest);
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

        var plan = changeRequest.Plan!;
        plan.Name = changeRequest.NewName;
        plan.Description = changeRequest.NewDescription;
        plan.MonthlyPrice = changeRequest.NewMonthlyPrice;
        plan.LocalRatePerMin = changeRequest.NewLocalRatePerMin;
        plan.InternationalRatePerMin = changeRequest.NewInternationalRatePerMin;
        plan.MinUsers = changeRequest.NewMinUsers;
        plan.MaxUsers = changeRequest.NewMaxUsers;
        plan.IsCustomQuote = changeRequest.NewIsCustomQuote;

        changeRequest.Status = "approved";
        changeRequest.ReviewedBy = User.GetUserId();
        changeRequest.ReviewedAt = DateTimeOffset.UtcNow;
        changeRequest.ReviewNote = request.Note;

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

        changeRequest.Status = "rejected";
        changeRequest.ReviewedBy = User.GetUserId();
        changeRequest.ReviewedAt = DateTimeOffset.UtcNow;
        changeRequest.ReviewNote = request.Note;

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "Price change rejected." });
    }

    [HttpPost("billing/run-cycle")]
    public async Task<IActionResult> RunBillingCycle(CancellationToken cancellationToken)
    {
        var generated = await _invoiceGenerationService.RunOnceAsync(cancellationToken);
        return Ok(new { message = $"{generated} invoice(s) generated." });
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
    public long TotalDurationSeconds { get; set; }
}

public class SignupPaymentRow
{
    public Guid CompanyId { get; set; }
    public string Status { get; set; } = "";
}
