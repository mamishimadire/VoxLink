namespace VoxLink.Api.Models;

public class Contact
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? CompanyName { get; set; }
    public string PhoneNumber { get; set; } = "";
    public string? Email { get; set; }
    public string? Notes { get; set; }
    public bool IsFavorite { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
