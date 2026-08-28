namespace VoxLink.Api.Models;

public class Plan
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal MonthlyPrice { get; set; }
    public int IncludedMinutes { get; set; }
    public decimal PerMinuteRate { get; set; }
    public decimal LocalRatePerMin { get; set; }
    public decimal InternationalRatePerMin { get; set; }
    public int MinUsers { get; set; }
    public int? MaxUsers { get; set; }
    public bool IsCustomQuote { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
}
