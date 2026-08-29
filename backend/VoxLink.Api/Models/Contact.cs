namespace VoxLink.Api.Models;

public class Contact
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    // Personal to whoever saved it — never shared with teammates in the same
    // company, unlike company-wide data such as billing or the team list.
    public Guid UserId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? CompanyName { get; set; }
    public string PhoneNumber { get; set; } = "";
    public string? Email { get; set; }
    public string? Notes { get; set; }
    public bool IsFavorite { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
