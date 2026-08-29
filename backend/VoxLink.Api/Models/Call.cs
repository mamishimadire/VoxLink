namespace VoxLink.Api.Models;

public class Call
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? PhoneNumberId { get; set; }
    public string DestinationNumber { get; set; } = "";
    public string Direction { get; set; } = "outbound";
    public string Status { get; set; } = "initiated";
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? AnsweredAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int DurationSeconds { get; set; }
    public string? ProviderCallId { get; set; }
    public decimal CarrierCost { get; set; }
    public decimal CustomerCharge { get; set; }
    public bool IsFavorite { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Hides a call from the user's own call-history view without removing
    // it from billing — invoice generation queries ignore this flag.
    public DateTimeOffset? DeletedAt { get; set; }
}
