using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxLink.Api.Auditing;
using VoxLink.Api.Auth;
using VoxLink.Api.Billing;
using VoxLink.Api.Data;
using VoxLink.Api.Models;

namespace VoxLink.Api.Controllers;

public record RegisterCompanyRequest(
    string CompanyName,
    string Phone,
    string Country,
    string Region,
    string AdminFirstName,
    string AdminLastName,
    string AdminEmail,
    string Password,
    Guid PlanId);

public record LoginRequest(string Email, string Password);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Token, string NewPassword);

public record AuthResponse(string Token, Guid UserId, Guid CompanyId, string Role);

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    // Auth has no company context to scope by yet (that's what it establishes),
    // so it uses the service context, which bypasses RLS, throughout.
    private readonly VoxLinkServiceDbContext _db;
    private readonly JwtTokenService _jwtTokenService;
    private readonly PasswordResetService _passwordResetService;
    private readonly SignupInvoiceService _signupInvoiceService;

    public AuthController(
        VoxLinkServiceDbContext db, JwtTokenService jwtTokenService, PasswordResetService passwordResetService, SignupInvoiceService signupInvoiceService)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
        _passwordResetService = passwordResetService;
        _signupInvoiceService = signupInvoiceService;
    }

    [HttpPost("register-company")]
    public async Task<ActionResult<AuthResponse>> RegisterCompany(RegisterCompanyRequest request, CancellationToken cancellationToken)
    {
        var emailInUse = await _db.Users.AnyAsync(u => u.Email == request.AdminEmail, cancellationToken);
        if (emailInUse)
        {
            return Conflict(new { message = "A user with that email already exists." });
        }

        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);
        if (plan is null)
        {
            return BadRequest(new { message = "Select a plan." });
        }

        var passwordError = PasswordPolicy.Validate(request.Password);
        if (passwordError is not null)
        {
            return BadRequest(new { message = passwordError });
        }

        // The very first account in the system is the platform's own bootstrap
        // owner and starts active, with no payment gate. Every company
        // registered after that is a prospective client — it starts "pending"
        // and needs its signup invoice paid + a platform admin's approval
        // before it can use the app, same as one onboarded manually.
        var isFirstAccountInSystem = !await _db.Users.AnyAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = request.CompanyName,
            Phone = request.Phone,
            Country = request.Country,
            Region = request.Region,
            Status = isFirstAccountInSystem ? "active" : "pending",
            IsInternal = isFirstAccountInSystem,
            CreatedAt = now,
            UpdatedAt = now
        };

        var admin = new User
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            FirstName = request.AdminFirstName,
            LastName = request.AdminLastName,
            Email = request.AdminEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = "owner",
            Status = "active",
            // The bootstrap account is VoxLink's own — both platform admin
            // (manages clients) and business owner (approves price changes).
            IsPlatformAdmin = isFirstAccountInSystem,
            IsBusinessOwner = isFirstAccountInSystem,
            PasswordChangedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Companies.Add(company);
        _db.Users.Add(admin);
        await _db.SaveChangesAsync(cancellationToken);

        if (!isFirstAccountInSystem)
        {
            await _signupInvoiceService.SelectPlanAsync(_db, company, plan, cancellationToken);
        }

        var token = _jwtTokenService.GenerateToken(admin);
        return Ok(new AuthResponse(token, admin.Id, company.Id, admin.Role));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);
        if (user is null)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        if (PasswordPolicy.IsLockedOut(user))
        {
            return Unauthorized(new { message = $"Account locked due to repeated failed attempts. Try again after {user.LockedUntil:HH:mm} UTC." });
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            PasswordPolicy.RegisterFailedAttempt(user);
            await _db.SaveChangesAsync(cancellationToken);
            return Unauthorized(new { message = "Invalid email or password." });
        }

        if (user.Status != "active")
        {
            return Unauthorized(new { message = "This account is not active." });
        }

        var companyStatus = await _db.Companies
            .Where(c => c.Id == user.CompanyId)
            .Select(c => c.Status)
            .FirstOrDefaultAsync(cancellationToken);

        // "pending" companies can still log in — they land in the onboarding
        // flow (pick a tier, pay, upload proof) rather than the full app.
        if (companyStatus is "suspended" or "cancelled" or "rejected")
        {
            return Unauthorized(new { message = "This company's access has been suspended." });
        }

        PasswordPolicy.RegisterSuccessfulLogin(user);
        AuditLogService.Log(_db, user.CompanyId, user.Id, user.Email, "auth.login", "user", user.Id, "Signed in");
        await _db.SaveChangesAsync(cancellationToken);

        var token = _jwtTokenService.GenerateToken(user);
        return Ok(new AuthResponse(token, user.Id, user.CompanyId, user.Role));
    }

    /// <summary>
    /// The JWT itself is stateless (nothing to revoke server-side) — this
    /// exists purely so "signed out" appears in the audit trail. The
    /// frontend calls it right before discarding its own token.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        AuditLogService.Log(_db, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "auth.logout", "user", User.GetUserId(), "Signed out");
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Unauthorized(new { message = "Current password is incorrect." });
        }

        var passwordError = PasswordPolicy.Validate(request.NewPassword);
        if (passwordError is not null)
        {
            return BadRequest(new { message = passwordError });
        }

        var now = DateTimeOffset.UtcNow;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.PasswordChangedAt = now;
        user.UpdatedAt = now;
        AuditLogService.Log(_db, user.CompanyId, user.Id, user.Email, "auth.password_changed", "user", user.Id,
            "Changed their own password");
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

        // Always return the same response, whether or not the email exists,
        // so this endpoint can't be used to enumerate registered accounts.
        if (user is not null && user.Status == "active")
        {
            await _passwordResetService.IssueAndSendAsync(_db, user, isNewAccount: false, cancellationToken);
        }

        return Ok(new { message = "If that email is registered, a reset link has been sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var tokenHash = PasswordResetTokenService.Hash(request.Token);
        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.PasswordResetTokenHash == tokenHash, cancellationToken);

        if (user is null || user.PasswordResetExpiresAt is null || user.PasswordResetExpiresAt < DateTimeOffset.UtcNow)
        {
            return BadRequest(new { message = "This reset link is invalid or has expired." });
        }

        var passwordError = PasswordPolicy.Validate(request.NewPassword);
        if (passwordError is not null)
        {
            return BadRequest(new { message = passwordError });
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.PasswordChangedAt = DateTimeOffset.UtcNow;
        user.PasswordResetTokenHash = null;
        user.PasswordResetExpiresAt = null;
        if (user.Status == "invited")
        {
            user.Status = "active";
        }
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}
