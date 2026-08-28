namespace VoxLink.Api.Models;

public class Subscription
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PlanId { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CurrentPeriodStart { get; set; }
    public DateTimeOffset CurrentPeriodEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Plan? Plan { get; set; }
}
