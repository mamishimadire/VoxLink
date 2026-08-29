namespace VoxLink.Api.Models;

public class User
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "employee";
    public string Status { get; set; } = "active";
    public bool IsPlatformAdmin { get; set; }
    public bool IsBusinessOwner { get; set; }
    public string? PasswordResetTokenHash { get; set; }
    public DateTimeOffset? PasswordResetExpiresAt { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? Gender { get; set; }
    public string? ProfilePicturePath { get; set; }
    public string Theme { get; set; } = "dark";

    public Company? Company { get; set; }
}
