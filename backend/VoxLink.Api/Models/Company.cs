namespace VoxLink.Api.Models;

public class Company
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? RegistrationNumber { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Status { get; set; } = "active";

    public string? PrimaryContactName { get; set; }
    public string? PrimaryContactEmail { get; set; }
    public string? BillingContactName { get; set; }
    public string? BillingContactEmail { get; set; }
    public string? AdminContactName { get; set; }
    public string? AdminContactEmail { get; set; }
    public Guid? SelectedPlanId { get; set; }
    public string? RejectedReason { get; set; }
    public bool IsInternal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Department> Departments { get; set; } = new List<Department>();
}
