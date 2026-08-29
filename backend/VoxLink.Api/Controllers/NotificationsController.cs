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

    public NotificationsController(VoxLinkDbContext db)
    {
        _db = db;
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
        }

        // The agreement is a client-company obligation, owner/admin only
        // (matches who's allowed to sign it in BillingController).
        if (role is "owner" or "admin")
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
