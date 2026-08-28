namespace VoxLink.Api.Models;

public class PlanChangeRequest
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public Guid ProposedBy { get; set; }
    public DateTimeOffset ProposedAt { get; set; }
    public string NewName { get; set; } = "";
    public string? NewDescription { get; set; }
    public decimal NewMonthlyPrice { get; set; }
    public int NewIncludedMinutes { get; set; }
    public decimal NewLocalRatePerMin { get; set; }
    public decimal NewInternationalRatePerMin { get; set; }
    public int NewMinUsers { get; set; }
    public int? NewMaxUsers { get; set; }
    public bool NewIsCustomQuote { get; set; }
    public string Status { get; set; } = "pending";
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }

    public Plan? Plan { get; set; }
}
