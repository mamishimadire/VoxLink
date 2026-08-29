using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxLink.Api.Auditing;
using VoxLink.Api.Auth;
using VoxLink.Api.Billing;
using VoxLink.Api.Data;

namespace VoxLink.Api.Controllers;

public record ReviewNoteRequest(string? Note);

/// <summary>
/// Deliberately NOT gated by the PlatformAdmin policy — a manager approving
/// a revoke or invoice-generation request must not also gain every other
/// PlatformController capability, so this checks role/company by hand and
/// uses the RLS-bypassing service context (see VoxLinkServiceDbContext's
/// doc comment) instead of relying on the is_platform_admin claim.
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

        AuditLogService.Log(_db, revokeRequest.CompanyId, User.GetUserId(), User.GetEmail(), "license_revoke.approved", "company", revokeRequest.CompanyId,
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

        AuditLogService.Log(_db, revokeRequest.CompanyId, User.GetUserId(), User.GetEmail(), "license_revoke.rejected", "company", revokeRequest.CompanyId,
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

            AuditLogService.Log(_db, generationRequest.CompanyId, User.GetUserId(), User.GetEmail(), "invoice.generated", "invoice", invoice.Id,
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

        AuditLogService.Log(_db, generationRequest.CompanyId, User.GetUserId(), User.GetEmail(), "invoice.generation_rejected", "company", generationRequest.CompanyId,
            $"Rejected manual invoice generation for {generationRequest.Company!.Name}: {request.Note}");
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Invoice generation request rejected." });
    }
}
