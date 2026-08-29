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
    // When the user last actually set their own password (register, reset
    // link, or self-service change) — drives the 30-day expiry policy.
    // NOT touched by an admin issuing a reset link; only by the password
    // actually being set.
    public DateTimeOffset PasswordChangedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? Gender { get; set; }
    public string? ProfilePicturePath { get; set; }
    public string Theme { get; set; } = "dark";
    // Who added this account — lets the admin who submitted it (as opposed
    // to the owner who approves it) see a "still pending" reminder of their
    // own submission.
    public Guid? CreatedBy { get; set; }

    public Company? Company { get; set; }
}
