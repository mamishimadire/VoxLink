using VoxLink.Api.Data;
using VoxLink.Api.Models;

namespace VoxLink.Api.Auditing;

/// <summary>
/// Adds an audit-log row to the same DbContext the caller is already
/// working with, so it's saved atomically alongside whatever change it's
/// recording (one SaveChangesAsync, not a separate round trip). companyId
/// should be whichever company the action affected — the target client for
/// a cross-tenant platform-admin action (e.g. approving that company), or
/// the actor's own company for something that isn't tied to a specific
/// client (e.g. VoxLink's own internal team or pricing changes) — so each
/// company's own audit log only ever shows what happened to/within it.
/// </summary>
public static class AuditLogService
{
    public static void Log(
        VoxLinkDbContext db,
        Guid? companyId,
        Guid? actorUserId,
        string? actorEmail,
        string action,
        string? entityType = null,
        Guid? entityId = null,
        string? details = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            UserId = actorUserId,
            ActorEmail = actorEmail,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
