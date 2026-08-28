using System.Text.RegularExpressions;

namespace VoxLink.Api.Auth;

public static class PasswordPolicy
{
    private const int MinLength = 10;
    private const int MaxFailedLoginAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Minimum 10 characters, at least one uppercase, one lowercase, one digit,
    /// and one special character. Returns null if valid, else a user-facing message.
    /// </summary>
    public static string? Validate(string password)
    {
        if (password.Length < MinLength)
            return $"Password must be at least {MinLength} characters long.";
        if (!Regex.IsMatch(password, "[A-Z]"))
            return "Password must contain at least one uppercase letter.";
        if (!Regex.IsMatch(password, "[a-z]"))
            return "Password must contain at least one lowercase letter.";
        if (!Regex.IsMatch(password, "[0-9]"))
            return "Password must contain at least one digit.";
        if (!Regex.IsMatch(password, "[^A-Za-z0-9]"))
            return "Password must contain at least one special character.";
        return null;
    }

    public static bool IsLockedOut(Models.User user) =>
        user.LockedUntil is not null && user.LockedUntil > DateTimeOffset.UtcNow;

    /// <summary>Call after a failed login attempt. Locks the account once the threshold is hit.</summary>
    public static void RegisterFailedAttempt(Models.User user)
    {
        user.FailedLoginAttempts += 1;
        if (user.FailedLoginAttempts >= MaxFailedLoginAttempts)
        {
            user.LockedUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
        }
    }

    public static void RegisterSuccessfulLogin(Models.User user)
    {
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
    }
}
