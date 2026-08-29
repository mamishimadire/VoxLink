using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxLink.Api.Auditing;
using VoxLink.Api.Auth;
using VoxLink.Api.Billing;
using VoxLink.Api.Data;
using VoxLink.Api.Models;

namespace VoxLink.Api.Controllers;

public record ReviewNoteRequest(string? Note);

/// <summary>
/// Deliberately NOT gated by the PlatformAdmin policy — a manager approving
/// a revoke, invoice-generation, or license-change request must not also
/// gain every other PlatformController capability, so this checks
/// role/company by hand and uses the RLS-bypassing service context (see
/// VoxLinkServiceDbContext's doc comment) instead of relying on the
/// is_platform_admin claim.
/// </summary>
[ApiController]
[Authorize]
[Route("api/approvals")]
public class ApprovalsController : ControllerBase
{
    private readonly VoxLinkServiceDbContext _db;
    private readonly InvoiceGenerationService _invoiceGenerationService;

    public ApprovalsController(VoxLinkServiceDbContext db, InvoiceGenerationService invoiceGenerationService)
    {
        _db = db;
        _invoiceGenerationService = invoiceGenerationService;
    }

    /// <summary>
    /// True only for a VoxLink-internal user — never a client company's own
    /// manager, who must never be able to approve VoxLink's actions against
    /// their own (or any other) company.
    /// </summary>
    private async Task<bool> IsInternalCallerAsync(CancellationToken cancellationToken)
    {
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == User.GetCompanyId(), cancellationToken);
        return company?.IsInternal == true;
    }

    [HttpGet("revoke-requests")]
    public async Task<IActionResult> GetRevokeRequests(CancellationToken cancellationToken)
    {
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole != "manager" || !await IsInternalCallerAsync(cancellationToken))
        {
            return Forbid();
        }

        var requests = await _db.LicenseRevokeRequests
            .Include(r => r.Company)
            .Where(r => r.Status == "pending" && r.ProposedByRole == "owner")
            .OrderByDescending(r => r.ProposedAt)
            .Select(r => new { r.Id, r.CompanyId, CompanyName = r.Company!.Name, r.ProposedByRole, r.Reason, r.ProposedAt })
            .ToListAsync(cancellationToken);

        return Ok(requests);
    }

    [HttpGet("invoice-generation-requests")]
    public async Task<IActionResult> GetInvoiceGenerationRequests(CancellationToken cancellationToken)
    {
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole != "manager" || !await IsInternalCallerAsync(cancellationToken))
        {
            return Forbid();
        }

        var requests = await _db.InvoiceGenerationRequests
            .Include(r => r.Company)
            .Where(r => r.Status == "pending")
            .OrderByDescending(r => r.ProposedAt)
            .Select(r => new { r.Id, r.CompanyId, CompanyName = r.Company!.Name, r.ProposedAt })
            .ToListAsync(cancellationToken);

        return Ok(requests);
    }

    [HttpGet("license-change-requests")]
    public async Task<IActionResult> GetLicenseChangeRequests(CancellationToken cancellationToken)
    {
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole != "manager" || !await IsInternalCallerAsync(cancellationToken))
        {
            return Forbid();
        }

        var requests = await _db.LicenseChangeRequests
            .Include(r => r.Company)
            .Include(r => r.Plan)
            .Where(r => r.Status == "pending" && r.ProposedByRole == "owner")
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

    [HttpPost("revoke-requests/{id:guid}/approve")]
    public async Task<IActionResult> ApproveRevoke(Guid id, ReviewNoteRequest request, CancellationToken cancellationToken)
    {
        var revokeRequest = await _db.LicenseRevokeRequests.Include(r => r.Company).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (revokeRequest is null) return NotFound();
        if (revokeRequest.Status != "pending") return BadRequest(new { message = "This request has already been reviewed." });

        // An admin's proposal needs an owner; an owner's proposal needs a
        // manager. Never the same person as the proposer.
        var requiredRole = revokeRequest.ProposedByRole == "admin" ? "owner" : "manager";
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole != requiredRole || revokeRequest.ProposedBy == User.GetUserId() || !await IsInternalCallerAsync(cancellationToken))
        {
            return Forbid();
        }

        revokeRequest.Status = "approved";
        revokeRequest.ReviewedBy = User.GetUserId();
        revokeRequest.ReviewedAt = DateTimeOffset.UtcNow;
        revokeRequest.ReviewNote = request.Note;
        revokeRequest.Company!.Status = "suspended";
        revokeRequest.Company.UpdatedAt = DateTimeOffset.UtcNow;

        AuditLogService.LogCrossTenant(_db, revokeRequest.CompanyId, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "license_revoke.approved", "company", revokeRequest.CompanyId,
            $"Approved revoking {revokeRequest.Company.Name}'s license");
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = $"{revokeRequest.Company.Name}'s license has been revoked." });
    }

    [HttpPost("revoke-requests/{id:guid}/reject")]
    public async Task<IActionResult> RejectRevoke(Guid id, ReviewNoteRequest request, CancellationToken cancellationToken)
    {
        var revokeRequest = await _db.LicenseRevokeRequests.Include(r => r.Company).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (revokeRequest is null) return NotFound();
        if (revokeRequest.Status != "pending") return BadRequest(new { message = "This request has already been reviewed." });

        var requiredRole = revokeRequest.ProposedByRole == "admin" ? "owner" : "manager";
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole != requiredRole || revokeRequest.ProposedBy == User.GetUserId() || !await IsInternalCallerAsync(cancellationToken))
        {
            return Forbid();
        }

        revokeRequest.Status = "rejected";
        revokeRequest.ReviewedBy = User.GetUserId();
        revokeRequest.ReviewedAt = DateTimeOffset.UtcNow;
        revokeRequest.ReviewNote = request.Note;

        AuditLogService.LogCrossTenant(_db, revokeRequest.CompanyId, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "license_revoke.rejected", "company", revokeRequest.CompanyId,
            $"Rejected revoking {revokeRequest.Company!.Name}'s license: {request.Note}");
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Revoke request rejected." });
    }

    [HttpPost("invoice-generation-requests/{id:guid}/approve")]
    public async Task<IActionResult> ApproveInvoiceGeneration(Guid id, ReviewNoteRequest request, CancellationToken cancellationToken)
    {
        var generationRequest = await _db.InvoiceGenerationRequests.Include(r => r.Company).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (generationRequest is null) return NotFound();
        if (generationRequest.Status != "pending") return BadRequest(new { message = "This request has already been reviewed." });

        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole != "manager" || generationRequest.ProposedBy == User.GetUserId() || !await IsInternalCallerAsync(cancellationToken))
        {
            return Forbid();
        }

        try
        {
            var invoice = await _invoiceGenerationService.GenerateAdHocInvoiceAsync(generationRequest.CompanyId, cancellationToken);

            generationRequest.Status = "approved";
            generationRequest.ReviewedBy = User.GetUserId();
            generationRequest.ReviewedAt = DateTimeOffset.UtcNow;
            generationRequest.ReviewNote = request.Note;
            generationRequest.GeneratedInvoiceId = invoice.Id;

            AuditLogService.LogCrossTenant(_db, generationRequest.CompanyId, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "invoice.generated", "invoice", invoice.Id,
                $"Approved manual invoice generation: {invoice.InvoiceNumber} for R{invoice.AmountDue:0.00}");
            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new { message = $"Invoice {invoice.InvoiceNumber} generated for R{invoice.AmountDue:0.00}.", invoice.Id, invoice.InvoiceNumber });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("invoice-generation-requests/{id:guid}/reject")]
    public async Task<IActionResult> RejectInvoiceGeneration(Guid id, ReviewNoteRequest request, CancellationToken cancellationToken)
    {
        var generationRequest = await _db.InvoiceGenerationRequests.Include(r => r.Company).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (generationRequest is null) return NotFound();
        if (generationRequest.Status != "pending") return BadRequest(new { message = "This request has already been reviewed." });

        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole != "manager" || generationRequest.ProposedBy == User.GetUserId() || !await IsInternalCallerAsync(cancellationToken))
        {
            return Forbid();
        }

        generationRequest.Status = "rejected";
        generationRequest.ReviewedBy = User.GetUserId();
        generationRequest.ReviewedAt = DateTimeOffset.UtcNow;
        generationRequest.ReviewNote = request.Note;

        AuditLogService.LogCrossTenant(_db, generationRequest.CompanyId, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "invoice.generation_rejected", "company", generationRequest.CompanyId,
            $"Rejected manual invoice generation for {generationRequest.Company!.Name}: {request.Note}");
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Invoice generation request rejected." });
    }

    [HttpPost("license-change-requests/{id:guid}/approve")]
    public async Task<IActionResult> ApproveLicenseChange(Guid id, ReviewNoteRequest request, CancellationToken cancellationToken)
    {
        var changeRequest = await _db.LicenseChangeRequests.Include(r => r.Company).Include(r => r.Plan).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (changeRequest is null) return NotFound();
        if (changeRequest.Status != "pending") return BadRequest(new { message = "This request has already been reviewed." });

        var requiredRole = changeRequest.ProposedByRole == "admin" ? "owner" : "manager";
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole != requiredRole || changeRequest.ProposedBy == User.GetUserId() || !await IsInternalCallerAsync(cancellationToken))
        {
            return Forbid();
        }

        var now = DateTimeOffset.UtcNow;
        var subscription = await _db.Subscriptions
            .Where(s => s.CompanyId == changeRequest.CompanyId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        // The platform fee for this first period may already have been paid
        // upfront via the signup invoice — don't charge it again on the
        // subscription's first real invoice if so.
        var hasPaidSignupInvoice = await _db.Invoices.AnyAsync(
            i => i.CompanyId == changeRequest.CompanyId && i.SubscriptionId == null && i.Status == "paid", cancellationToken);

        if (subscription is null)
        {
            subscription = new Subscription
            {
                Id = Guid.NewGuid(),
                CompanyId = changeRequest.CompanyId,
                PlanId = changeRequest.PlanId,
                Status = "active",
                CurrentPeriodStart = now,
                CurrentPeriodEnd = changeRequest.ExpiresAt,
                CurrentPeriodFeeBilled = hasPaidSignupInvoice,
                CreatedAt = now
            };
            _db.Subscriptions.Add(subscription);
        }
        else
        {
            subscription.PlanId = changeRequest.PlanId;
            subscription.Status = "active";
            subscription.CurrentPeriodStart = now;
            subscription.CurrentPeriodEnd = changeRequest.ExpiresAt;
            // A re-license starts a fresh period from now — neither the fee
            // nor the included-minutes pool has been billed against it yet.
            subscription.CurrentPeriodFeeBilled = false;
            subscription.CurrentPeriodLocalMinutesBilled = 0;
        }

        changeRequest.Status = "approved";
        changeRequest.ReviewedBy = User.GetUserId();
        changeRequest.ReviewedAt = now;
        changeRequest.ReviewNote = request.Note;

        AuditLogService.LogCrossTenant(_db, changeRequest.CompanyId, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "license_change.approved", "company", changeRequest.CompanyId,
            $"Approved setting {changeRequest.Company!.Name} to {changeRequest.Plan!.Name}, expires {changeRequest.ExpiresAt:yyyy-MM-dd}");
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = $"{changeRequest.Plan.Name} license set for {changeRequest.Company.Name}, expires {changeRequest.ExpiresAt:yyyy-MM-dd}." });
    }

    [HttpPost("license-change-requests/{id:guid}/reject")]
    public async Task<IActionResult> RejectLicenseChange(Guid id, ReviewNoteRequest request, CancellationToken cancellationToken)
    {
        var changeRequest = await _db.LicenseChangeRequests.Include(r => r.Company).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (changeRequest is null) return NotFound();
        if (changeRequest.Status != "pending") return BadRequest(new { message = "This request has already been reviewed." });

        var requiredRole = changeRequest.ProposedByRole == "admin" ? "owner" : "manager";
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole != requiredRole || changeRequest.ProposedBy == User.GetUserId() || !await IsInternalCallerAsync(cancellationToken))
        {
            return Forbid();
        }

        changeRequest.Status = "rejected";
        changeRequest.ReviewedBy = User.GetUserId();
        changeRequest.ReviewedAt = DateTimeOffset.UtcNow;
        changeRequest.ReviewNote = request.Note;

        AuditLogService.LogCrossTenant(_db, changeRequest.CompanyId, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "license_change.rejected", "company", changeRequest.CompanyId,
            $"Rejected a license change for {changeRequest.Company!.Name}: {request.Note}");
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "License change request rejected." });
    }
}
