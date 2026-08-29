namespace VoxLink.Api.Models;

public class LicenseRevokeRequest
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ProposedBy { get; set; }
    // Determines who must approve: an admin's proposal needs an owner, an
    // owner's proposal needs a manager — neither can approve their own.
    public string ProposedByRole { get; set; } = "";
    public string? Reason { get; set; }
    public DateTimeOffset ProposedAt { get; set; }
    public string Status { get; set; } = "pending";
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }

    public Company? Company { get; set; }
}
