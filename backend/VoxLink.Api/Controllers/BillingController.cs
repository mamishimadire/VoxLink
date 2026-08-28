using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VoxLink.Api.Auth;
using VoxLink.Api.Billing;
using VoxLink.Api.Data;
using VoxLink.Api.Models;
using VoxLink.Api.Pdf;
using VoxLink.Api.Storage;

namespace VoxLink.Api.Controllers;

public record SignAgreementRequest(string FullName, bool Agree);

public record SelectPlanRequest(Guid PlanId);

public record OnboardingStatusResponse(
    string CompanyStatus,
    string? SelectedPlanName,
    Guid? SignupInvoiceId,
    decimal? SignupInvoiceAmount,
    string? SignupPaymentStatus);

public record UsageResponse(
    string? PlanName, int IncludedMinutes, decimal LocalMinutesUsed, decimal InternationalMinutesUsed,
    decimal LocalRatePerMin, decimal InternationalRatePerMin, int CallCount, decimal EstimatedAmountDue,
    DateTimeOffset? PeriodStart, DateTimeOffset? PeriodEnd, int? MaxUsers, int CurrentUserCount);

public record UserUsageRow(Guid UserId, string UserName, int CallCount, decimal TotalMinutes);

public record DestinationUsageRow(string DestinationNumber, int CallCount, decimal TotalMinutes);

public record AnalyticsResponse(DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd, List<UserUsageRow> ByUser, List<DestinationUsageRow> ByDestination);

[ApiController]
[Authorize]
[Route("api/billing")]
public class BillingController : ControllerBase
{
    private const string TermsVersion = "2026-08-v1";

    private readonly VoxLinkDbContext _db;
    private readonly SupabaseStorageClient _storage;
    private readonly BillingOptions _billingOptions;
    private readonly SignupInvoiceService _signupInvoiceService;
    private readonly InvoiceGenerationService _invoiceGenerationService;

    public BillingController(
        VoxLinkDbContext db, SupabaseStorageClient storage, IOptions<BillingOptions> billingOptions,
        SignupInvoiceService signupInvoiceService, InvoiceGenerationService invoiceGenerationService)
    {
        _db = db;
        _storage = storage;
        _billingOptions = billingOptions.Value;
        _signupInvoiceService = signupInvoiceService;
        _invoiceGenerationService = invoiceGenerationService;
    }

    [HttpGet("plans")]
    public Task<IActionResult> GetPlans(CancellationToken cancellationToken) => GetPlansInternal(cancellationToken);

    // Unauthenticated: the registration form needs to show tiers before signup.
    [HttpGet("/api/plans")]
    [AllowAnonymous]
    public Task<IActionResult> GetPublicPlans(CancellationToken cancellationToken) => GetPlansInternal(cancellationToken);

    private async Task<IActionResult> GetPlansInternal(CancellationToken cancellationToken)
    {
        var plans = await _db.Plans
            .Where(p => p.Status == "active")
            .OrderBy(p => p.MinUsers)
            .Select(p => new { p.Id, p.Name, p.Description, p.MonthlyPrice, p.LocalRatePerMin, p.InternationalRatePerMin, p.IncludedMinutes, p.MinUsers, p.MaxUsers, p.IsCustomQuote })
            .ToListAsync(cancellationToken);

        return Ok(plans);
    }

    [HttpGet("onboarding-status")]
    public async Task<ActionResult<OnboardingStatusResponse>> GetOnboardingStatus(CancellationToken cancellationToken)
    {
        var companyId = User.GetCompanyId();
        var company = await _db.Companies.FirstAsync(c => c.Id == companyId, cancellationToken);

        string? planName = null;
        if (company.SelectedPlanId is Guid planId)
        {
            planName = await _db.Plans.Where(p => p.Id == planId).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken);
        }

        var signupInvoice = await _db.Invoices
            .Where(i => i.CompanyId == companyId && i.SubscriptionId == null)
            .OrderByDescending(i => i.IssuedAt)
            .FirstOrDefaultAsync(cancellationToken);

        string? paymentStatus = null;
        if (signupInvoice is not null)
        {
            paymentStatus = await _db.Payments
                .Where(p => p.InvoiceId == signupInvoice.Id)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => p.Status)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return Ok(new OnboardingStatusResponse(
            company.Status, planName, signupInvoice?.Id, signupInvoice?.AmountDue, paymentStatus));
    }

    [HttpPost("select-plan")]
    public async Task<IActionResult> SelectPlan(SelectPlanRequest request, CancellationToken cancellationToken)
    {
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole is not ("owner" or "admin"))
        {
            return Forbid();
        }

        var companyId = User.GetCompanyId();
        var company = await _db.Companies.FirstAsync(c => c.Id == companyId, cancellationToken);
        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);
        if (plan is null) return NotFound(new { message = "Plan not found." });

        var message = await _signupInvoiceService.SelectPlanAsync(_db, company, plan, cancellationToken);
        return Ok(new { message });
    }

    [HttpGet("agreement")]
    public async Task<IActionResult> GetAgreement(CancellationToken cancellationToken)
    {
        var companyId = User.GetCompanyId();
        var agreement = await _db.ServiceAgreements
            .Where(a => a.CompanyId == companyId)
            .OrderByDescending(a => a.AgreedAt)
            .Select(a => new { a.AgreedByName, a.AgreedAt, a.TermsVersion })
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(new { signed = agreement is not null, agreement });
    }

    [HttpPost("agreement/sign")]
    public async Task<IActionResult> SignAgreement(SignAgreementRequest request, CancellationToken cancellationToken)
    {
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole is not ("owner" or "admin"))
        {
            return Forbid();
        }

        if (!request.Agree || string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { message = "You must type your full name and check the agreement box." });
        }

        var companyId = User.GetCompanyId();
        var company = await _db.Companies.FirstAsync(c => c.Id == companyId, cancellationToken);
        var email = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value ?? "";
        var now = DateTimeOffset.UtcNow;
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        var pdfBytes = AgreementPdfGenerator.Generate(company.Name, TermsVersion, request.FullName, email, now, ip);
        var storagePath = $"agreements/{companyId}/{Guid.NewGuid()}.pdf";
        await _storage.UploadAsync(storagePath, pdfBytes, "application/pdf", cancellationToken);

        _db.ServiceAgreements.Add(new ServiceAgreement
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            TermsVersion = TermsVersion,
            AgreedByName = request.FullName,
            AgreedByEmail = email,
            AgreedAt = now,
            IpAddress = ip,
            PdfStoragePath = storagePath,
            CreatedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Agreement signed." });
    }

    [HttpGet("usage")]
    public async Task<IActionResult> GetUsage(CancellationToken cancellationToken)
    {
        var companyId = User.GetCompanyId();

        var currentUserCount = await _db.Users.CountAsync(u => u.CompanyId == companyId && u.Status != "suspended", cancellationToken);

        var subscription = await _db.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.CompanyId == companyId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            return Ok(new UsageResponse(null, 0, 0, 0, 0, 0, 0, 0, null, null, null, currentUserCount));
        }

        var calls = await _db.Calls
            .Where(c => c.CompanyId == companyId
                && c.CreatedAt >= subscription.CurrentPeriodStart
                && c.CreatedAt < subscription.CurrentPeriodEnd)
            .Select(c => new CallUsageRow(c.DestinationNumber, c.DurationSeconds))
            .ToListAsync(cancellationToken);

        var plan = subscription.Plan!;
        var usage = UsageCalculator.Compute(calls, plan, _billingOptions.LocalCountryCode);

        return Ok(new UsageResponse(
            plan.Name, plan.IncludedMinutes, usage.LocalMinutes, usage.InternationalMinutes,
            plan.LocalRatePerMin, plan.InternationalRatePerMin, usage.CallCount, usage.AmountDue,
            subscription.CurrentPeriodStart, subscription.CurrentPeriodEnd, plan.MaxUsers, currentUserCount));
    }

    /// <summary>
    /// Who is calling the most, and which numbers are being called the
    /// most — lets an admin spot overuse or misuse of the calling resource
    /// (e.g. one user racking up far more external minutes than the rest of
    /// the team) instead of only seeing a single company-wide total.
    /// </summary>
    [HttpGet("analytics")]
    public async Task<ActionResult<AnalyticsResponse>> GetAnalytics(CancellationToken cancellationToken)
    {
        var companyId = User.GetCompanyId();

        var subscription = await _db.Subscriptions
            .Where(s => s.CompanyId == companyId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        // Companies with no plan yet (or VoxLink's own internal team, which
        // never has one) still make real calls that cost real money —
        // default to a trailing 30-day window so there's still something to see.
        var periodStart = subscription?.CurrentPeriodStart ?? DateTimeOffset.UtcNow.AddDays(-30);
        var periodEnd = subscription?.CurrentPeriodEnd ?? DateTimeOffset.UtcNow;

        var calls = await _db.Calls
            .Where(c => c.CompanyId == companyId && c.CreatedAt >= periodStart && c.CreatedAt < periodEnd)
            .Select(c => new { c.UserId, c.DestinationNumber, c.DurationSeconds })
            .ToListAsync(cancellationToken);

        var userIds = calls.Where(c => c.UserId is not null).Select(c => c.UserId!.Value).Distinct().ToList();
        var userNames = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}", cancellationToken);

        var byUser = calls
            .Where(c => c.UserId is not null)
            .GroupBy(c => c.UserId!.Value)
            .Select(g => new UserUsageRow(
                g.Key,
                userNames.GetValueOrDefault(g.Key, "Unknown"),
                g.Count(),
                Math.Ceiling(g.Sum(c => c.DurationSeconds) / 60m)))
            .OrderByDescending(r => r.TotalMinutes)
            .ToList();

        var byDestination = calls
            .GroupBy(c => c.DestinationNumber)
            .Select(g => new DestinationUsageRow(g.Key, g.Count(), Math.Ceiling(g.Sum(c => c.DurationSeconds) / 60m)))
            .OrderByDescending(r => r.TotalMinutes)
            .Take(20)
            .ToList();

        return Ok(new AnalyticsResponse(periodStart, periodEnd, byUser, byDestination));
    }

    [HttpGet("invoices")]
    public async Task<IActionResult> GetInvoices(
        [FromQuery] string? number, [FromQuery] string? status,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? year,
        CancellationToken cancellationToken)
    {
        var companyId = User.GetCompanyId();
        var query = _db.Invoices.Where(i => i.CompanyId == companyId);

        if (!string.IsNullOrWhiteSpace(number))
        {
            query = query.Where(i => EF.Functions.ILike(i.InvoiceNumber, $"%{number.Trim()}%"));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(i => i.Status == status);
        }
        if (year is int y)
        {
            query = query.Where(i => i.IssuedAt.Year == y);
        }
        if (from is DateOnly f)
        {
            var fromUtc = new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(i => i.IssuedAt >= fromUtc);
        }
        if (to is DateOnly t)
        {
            var toUtc = new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(i => i.IssuedAt < toUtc);
        }

        var invoices = await query
            .OrderByDescending(i => i.IssuedAt)
            .Select(i => new { i.Id, i.InvoiceNumber, i.AmountDue, i.AmountPaid, i.Status, i.DueDate, i.IssuedAt })
            .ToListAsync(cancellationToken);

        return Ok(invoices);
    }

    /// <summary>
    /// Owner/admin-triggered invoice covering usage from the current
    /// subscription period's start through now — for a client that wants an
    /// early invoice, or for VoxLink's own internal team checking what their
    /// call usage is costing them without waiting for month-end.
    /// </summary>
    [HttpPost("invoices/generate")]
    public async Task<IActionResult> GenerateInvoice(CancellationToken cancellationToken)
    {
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole is not ("owner" or "admin"))
        {
            return Forbid();
        }

        var companyId = User.GetCompanyId();
        try
        {
            var invoice = await _invoiceGenerationService.GenerateAdHocInvoiceAsync(companyId, cancellationToken);
            return Ok(new { message = $"Invoice {invoice.InvoiceNumber} generated for R{invoice.AmountDue:0.00}.", invoice.Id, invoice.InvoiceNumber });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("invoices/{id:guid}/pdf")]
    public async Task<IActionResult> GetInvoicePdf(Guid id, CancellationToken cancellationToken)
    {
        var companyId = User.GetCompanyId();
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == companyId, cancellationToken);
        if (invoice?.PdfStoragePath is null) return NotFound();

        var url = await _storage.GetSignedUrlAsync(invoice.PdfStoragePath, 300, cancellationToken);
        return Ok(new { url });
    }

    [HttpPost("invoices/{id:guid}/proof")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadProofOfPayment(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole is not ("owner" or "admin"))
        {
            return Forbid();
        }

        var companyId = User.GetCompanyId();
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == companyId, cancellationToken);
        if (invoice is null) return NotFound();

        if (file.Length == 0) return BadRequest(new { message = "No file uploaded." });

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);

        var storagePath = $"payment-proofs/{companyId}/{invoice.Id}/{Guid.NewGuid()}-{file.FileName}";
        await _storage.UploadAsync(storagePath, memoryStream.ToArray(), file.ContentType, cancellationToken);

        _db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            InvoiceId = invoice.Id,
            Amount = invoice.AmountDue,
            Method = "bank_transfer",
            Status = "submitted",
            ProofFilePath = storagePath,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Proof of payment submitted. It will be verified shortly." });
    }
}
