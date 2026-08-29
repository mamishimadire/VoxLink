using Microsoft.EntityFrameworkCore;
using VoxLink.Api.Models;

namespace VoxLink.Api.Auditing;

/// <summary>
/// Adds an audit-log row to the same DbContext the caller is already
/// working with, so it's saved atomically alongside whatever change it's
/// recording (one SaveChangesAsync, not a separate round trip). Takes the
/// base DbContext type (not VoxLinkDbContext specifically) so it also works
/// from AuthController, which uses VoxLinkServiceDbContext — there's no
/// company/session context yet during login/register, so RLS wouldn't
/// apply to the write anyway. companyId should be whichever company the
/// action affected — the target client for a cross-tenant platform-admin
/// action (e.g. approving that company), or the actor's own company for
/// something that isn't tied to a specific client (e.g. VoxLink's own
/// internal team or pricing changes) — so each company's own audit log
/// only ever shows what happened to/within it.
/// </summary>
public static class AuditLogService
{
    public static void Log(
        DbContext db,
        Guid? companyId,
        Guid? actorUserId,
        string? actorEmail,
        string action,
        string? entityType = null,
        Guid? entityId = null,
        string? details = null)
    {
        db.Set<AuditLog>().Add(new AuditLog
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

    /// <summary>
    /// For a platform-admin action taken against a specific client company
    /// (approving/rejecting it, revoking or changing its license, verifying
    /// its payment, generating its invoice): writes TWO rows for the one
    /// event — one under the client's own company (so they can see what
    /// VoxLink did to their account) and one under the acting staff
    /// member's own company (so VoxLink's own internal audit log shows what
    /// its own team did — otherwise an action like a manager's approval
    /// would only ever show up in the client's log, never VoxLink's own).
    /// No-ops the second write if the two companies are the same (there's
    /// no real cross-tenant action to record twice).
    /// </summary>
    public static void LogCrossTenant(
        DbContext db,
        Guid targetCompanyId,
        Guid actorCompanyId,
        Guid? actorUserId,
        string? actorEmail,
        string action,
        string? entityType = null,
        Guid? entityId = null,
        string? details = null)
    {
        Log(db, targetCompanyId, actorUserId, actorEmail, action, entityType, entityId, details);
        if (actorCompanyId != targetCompanyId)
        {
            Log(db, actorCompanyId, actorUserId, actorEmail, action, entityType, entityId, details);
        }
    }
}
