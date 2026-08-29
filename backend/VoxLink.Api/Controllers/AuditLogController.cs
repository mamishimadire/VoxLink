using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxLink.Api.Auth;
using VoxLink.Api.Data;

namespace VoxLink.Api.Controllers;

/// <summary>
/// One shared endpoint for both sides: a client owner sees their own
/// company's log, and a VoxLink owner sees VoxLink's own internal log —
/// same query, scoped by the caller's own company_id, so there's no
/// separate "platform audit log" view to keep in sync with this one.
/// </summary>
[ApiController]
[Authorize]
[Route("api/audit-log")]
public class AuditLogController : ControllerBase
{
    private readonly VoxLinkDbContext _db;

    public AuditLogController(VoxLinkDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAuditLog(CancellationToken cancellationToken)
    {
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole != "owner")
        {
            return Forbid();
        }

        var companyId = User.GetCompanyId();
        var logs = await _db.AuditLogs
            .Where(a => a.CompanyId == companyId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(500)
            .Select(a => new
            {
                a.Id,
                a.Action,
                a.EntityType,
                a.EntityId,
                a.Details,
                a.ActorEmail,
                a.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(logs);
    }
}
