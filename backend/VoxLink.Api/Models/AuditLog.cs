namespace VoxLink.Api.Models;

public class AuditLog
{
    public Guid Id { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? UserId { get; set; }
    // Captured as plain text at write time (not read via a join to Users) so
    // the record stays legible even if the acting user is later deleted, and
    // so a client owner reading their own log isn't blocked by RLS from
    // seeing who at VoxLink acted on their account.
    public string? ActorEmail { get; set; }
    public string Action { get; set; } = "";
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
