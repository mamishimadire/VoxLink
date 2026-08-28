namespace VoxLink.Api.Models;

public class Department
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }

    public Company? Company { get; set; }
}
