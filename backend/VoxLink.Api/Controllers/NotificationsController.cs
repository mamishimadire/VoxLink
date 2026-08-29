using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxLink.Api.Auth;
using VoxLink.Api.Data;

namespace VoxLink.Api.Controllers;

public record NotificationItem(string Id, string Type, string Message);

/// <summary>
/// Everything in the system that is waiting on someone's action, scoped to
/// what THIS caller specifically has authority to act on — so nothing that
/// needs a decision sits unnoticed until someone happens to open the right
/// tab. Each section below mirrors an existing approve/reject/sign action
/// elsewhere in the app; this just surfaces it proactively.
/// </summary>
[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly VoxLinkDbContext _db;
    private readonly VoxLinkServiceDbContext _serviceDb;

    public NotificationsController(VoxLinkDbContext db, VoxLinkServiceDbContext serviceDb)
    {
        _db = db;
        _serviceDb = serviceDb;
    }

    [HttpGet]
    public async Task<IActionResult> GetNotifications(CancellationToken cancellationToken)
    {
        var items = new List<NotificationItem>();
        var companyId = User.GetCompanyId();
        var userId = User.GetUserId();
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var isPlatformAdmin = User.IsPlatformAdmin();
        var isBusinessOwner = User.IsBusinessOwner();

        // Applies to every user regardless of role — password hygiene isn't
        // an admin-only concern. Self-service change (Profile page) is the
        // fast path this is meant to point people at, instead of waiting on
        // an automatic reset email or an admin-triggered reset.
        var passwordChangedAt = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.PasswordChangedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var passwordAgeDays = (int)(DateTimeOffset.UtcNow - passwordChangedAt).TotalDays;
        if (passwordAgeDays >= PasswordPolicy.MaxPasswordAgeDays)
        {
            items.Add(new NotificationItem(
                "password-expired", "password_expired",
                "Your password has expired. Change it now on your Profile page to keep your account secure."));
        }
        else if (passwordAgeDays >= PasswordPolicy.MaxPasswordAgeDays - PasswordPolicy.PasswordExpiryWarningDays)
        {
            var daysLeft = PasswordPolicy.MaxPasswordAgeDays - passwordAgeDays;
            items.Add(new NotificationItem(
                "password-expiring", "password_expiring",
                $"Your password expires in {daysLeft} day{(daysLeft == 1 ? "" : "s")} — change it now on your Profile page."));
        }

        // A teammate an admin added, waiting on this owner specifically —
        // applies the same whether it's VoxLink's own internal team or a
        // client company's.
        if (role == "owner")
        {
            var pendingUsers = await _db.Users
                .Where(u => u.CompanyId == companyId && u.Status == "pending_approval")
                .Select(u => new { u.Id, u.FirstName, u.LastName })
                .ToListAsync(cancellationToken);

            items.AddRange(pendingUsers.Select(u =>
                new NotificationItem($"user:{u.Id}", "user_approval", $"{u.FirstName} {u.LastName} is awaiting your approval to join the team")));
        }

        // Price changes are VoxLink-internal only — only ever populated for
        // VoxLink's own business owner. Excludes the caller's own proposals,
        // since they can't approve those anyway.
        if (isBusinessOwner)
        {
            var pendingChanges = await _db.PlanChangeRequests
                .Include(r => r.Plan)
                .Where(r => r.Status == "pending" && r.ProposedBy != userId)
                .Select(r => new { r.Id, PlanName = r.Plan!.Name })
                .ToListAsync(cancellationToken);

            items.AddRange(pendingChanges.Select(r =>
                new NotificationItem($"price:{r.Id}", "price_change", $"A price change for {r.PlanName} is awaiting your approval")));

            // An admin's revoke proposal needs an owner (never a manager) —
            // an owner's own proposal is handled in the manager section below.
            var pendingRevokesForOwner = await _db.LicenseRevokeRequests
                .Include(r => r.Company)
                .Where(r => r.Status == "pending" && r.ProposedByRole == "admin" && r.ProposedBy != userId)
                .Select(r => new { r.Id, CompanyName = r.Company!.Name })
                .ToListAsync(cancellationToken);

            items.AddRange(pendingRevokesForOwner.Select(r =>
                new NotificationItem($"revoke:{r.Id}", "revoke_approval", $"A request to revoke {r.CompanyName}'s license is awaiting your approval")));
        }

        if (isPlatformAdmin)
        {
            var pendingCompanies = await _db.Companies
                .Where(c => !c.IsInternal && c.Status == "pending")
                .Select(c => new { c.Id, c.Name })
                .ToListAsync(cancellationToken);

            items.AddRange(pendingCompanies.Select(c =>
                new NotificationItem($"company:{c.Id}", "company_approval", $"{c.Name} is awaiting approval")));

            var pendingPayments = await _db.Payments
                .Where(p => p.Status == "submitted")
                .Select(p => new { p.Id, CompanyName = p.Company!.Name })
                .ToListAsync(cancellationToken);

            items.AddRange(pendingPayments.Select(p =>
                new NotificationItem($"payment:{p.Id}", "payment_verification", $"Payment proof from {p.CompanyName} is awaiting verification")));

            // FYI, not actionable: whoever proposed a revoke or a manual
            // invoice has no authority to review their own submission, but
            // shouldn't be left wondering whether it's been seen.
            var ownPendingRevokes = await _db.LicenseRevokeRequests
                .Include(r => r.Company)
                .Where(r => r.Status == "pending" && r.ProposedBy == userId)
                .Select(r => new { r.Id, CompanyName = r.Company!.Name, r.ProposedByRole })
                .ToListAsync(cancellationToken);

            items.AddRange(ownPendingRevokes.Select(r =>
            {
                var approver = r.ProposedByRole == "admin" ? "an owner" : "a manager";
                return new NotificationItem($"revoke-fyi:{r.Id}", "revoke_pending", $"Your request to revoke {r.CompanyName}'s license is still awaiting approval from {approver}");
            }));

            var ownPendingInvoiceRequests = await _db.InvoiceGenerationRequests
                .Include(r => r.Company)
                .Where(r => r.Status == "pending" && r.ProposedBy == userId)
                .Select(r => new { r.Id, CompanyName = r.Company!.Name })
                .ToListAsync(cancellationToken);

            items.AddRange(ownPendingInvoiceRequests.Select(r =>
                new NotificationItem($"invoice-gen-fyi:{r.Id}", "invoice_generation_pending", $"Your invoice generation request for {r.CompanyName} is still awaiting a manager's approval")));
        }

        // A manager doesn't carry the platform-admin claim (deliberately —
        // see VoxLinkServiceDbContext's doc comment), so RLS would block
        // these cross-company lookups via the regular context even though
        // the action is authorized; the service context bypasses that the
        // same way ApprovalsController does.
        if (role == "manager")
        {
            var callerCompany = await _serviceDb.Companies.FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
            if (callerCompany?.IsInternal == true)
            {
                var pendingRevokesForManager = await _serviceDb.LicenseRevokeRequests
                    .Include(r => r.Company)
                    .Where(r => r.Status == "pending" && r.ProposedByRole == "owner")
                    .Select(r => new { r.Id, CompanyName = r.Company!.Name })
                    .ToListAsync(cancellationToken);

                items.AddRange(pendingRevokesForManager.Select(r =>
                    new NotificationItem($"revoke:{r.Id}", "revoke_approval", $"A request to revoke {r.CompanyName}'s license is awaiting your approval")));

                var pendingInvoiceRequests = await _serviceDb.InvoiceGenerationRequests
                    .Include(r => r.Company)
                    .Where(r => r.Status == "pending")
                    .Select(r => new { r.Id, CompanyName = r.Company!.Name })
                    .ToListAsync(cancellationToken);

                items.AddRange(pendingInvoiceRequests.Select(r =>
                    new NotificationItem($"invoice-gen:{r.Id}", "invoice_generation_approval", $"An invoice generation request for {r.CompanyName} is awaiting your approval")));
            }
        }

        // FYI, not actionable: the admin who submitted something has no
        // authority to approve their own submission, but shouldn't be left
        // wondering whether it's been seen — same whether it's VoxLink's own
        // internal team or a client company's.
        if (role == "admin")
        {
            var ownPendingUsers = await _db.Users
                .Where(u => u.CompanyId == companyId && u.Status == "pending_approval" && u.CreatedBy == userId)
                .Select(u => new { u.Id, u.FirstName, u.LastName })
                .ToListAsync(cancellationToken);

            items.AddRange(ownPendingUsers.Select(u =>
                new NotificationItem($"user-fyi:{u.Id}", "user_approval_pending", $"{u.FirstName} {u.LastName} is still awaiting owner approval")));

            // Only ever populated for VoxLink's own admin — client admins
            // never propose price changes (that endpoint is platform-admin
            // only), so this naturally never shows for a client company.
            var ownPendingChanges = await _db.PlanChangeRequests
                .Include(r => r.Plan)
                .Where(r => r.Status == "pending" && r.ProposedBy == userId)
                .Select(r => new { r.Id, PlanName = r.Plan!.Name })
                .ToListAsync(cancellationToken);

            items.AddRange(ownPendingChanges.Select(r =>
                new NotificationItem($"price-fyi:{r.Id}", "price_change_pending", $"Your proposed price change for {r.PlanName} is still awaiting a business owner's review")));
        }

        // The agreement is a client-company obligation, owner only — a
        // legal commitment, not a day-to-day admin action (matches who's
        // allowed to sign it in BillingController).
        if (role == "owner")
        {
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
            if (company is { IsInternal: false, Status: "active" })
            {
                var hasAgreement = await _db.ServiceAgreements.AnyAsync(a => a.CompanyId == companyId, cancellationToken);
                if (!hasAgreement)
                {
                    items.Add(new NotificationItem("agreement", "agreement_unsigned", "Your company's service agreement still needs to be signed"));
                }
            }
        }

        return Ok(new { items });
    }
}
