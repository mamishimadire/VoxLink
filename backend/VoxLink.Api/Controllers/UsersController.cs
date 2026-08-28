using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxLink.Api.Auth;
using VoxLink.Api.Data;
using VoxLink.Api.Models;
using VoxLink.Api.Storage;

namespace VoxLink.Api.Controllers;

public record CreateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string Role,
    Guid? DepartmentId);

public record UpdateProfileRequest(string FirstName, string LastName, string? Country, string? Region, string? Gender);

public record ProfileResponse(
    Guid Id, string FirstName, string LastName, string Email, string? Country, string? Region, string? Gender, string? PhotoUrl);

[ApiController]
[Authorize]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private static readonly string[] AssignableRoles = ["admin", "manager", "employee"];

    private readonly VoxLinkDbContext _db;
    private readonly PasswordResetService _passwordResetService;
    private readonly SupabaseStorageClient _storage;

    public UsersController(VoxLinkDbContext db, PasswordResetService passwordResetService, SupabaseStorageClient storage)
    {
        _db = db;
        _passwordResetService = passwordResetService;
        _storage = storage;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == User.GetUserId(), cancellationToken);
        if (user is null) return NotFound();

        var photoUrl = user.ProfilePicturePath is null
            ? null
            : await _storage.GetSignedUrlAsync(user.ProfilePicturePath, 3600, cancellationToken);

        return Ok(new ProfileResponse(user.Id, user.FirstName, user.LastName, user.Email, user.Country, user.Region, user.Gender, photoUrl));
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == User.GetUserId(), cancellationToken);
        if (user is null) return NotFound();

        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            return BadRequest(new { message = "First and last name are required." });
        }

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.Country = request.Country;
        user.Region = request.Region;
        user.Gender = request.Gender;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Profile updated." });
    }

    [HttpPost("me/photo")]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> UploadPhoto(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0) return BadRequest(new { message = "No file uploaded." });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == User.GetUserId(), cancellationToken);
        if (user is null) return NotFound();

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);

        var storagePath = $"profile-pictures/{user.Id}/{Guid.NewGuid()}{StoragePaths.SafeExtension(file.FileName)}";
        await _storage.UploadAsync(storagePath, memoryStream.ToArray(), file.ContentType, cancellationToken);

        user.ProfilePicturePath = storagePath;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var photoUrl = await _storage.GetSignedUrlAsync(storagePath, 3600, cancellationToken);
        return Ok(new { photoUrl });
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var companyId = User.GetCompanyId();
        var users = await _db.Users
            .Where(u => u.CompanyId == companyId)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.Role, u.Status, u.DepartmentId })
            .ToListAsync(cancellationToken);

        return Ok(users);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole is not ("owner" or "admin"))
        {
            return Forbid();
        }

        if (!AssignableRoles.Contains(request.Role))
        {
            return BadRequest(new { message = $"Role must be one of: {string.Join(", ", AssignableRoles)}" });
        }

        var companyId = User.GetCompanyId();

        var emailInUse = await _db.Users.AnyAsync(u => u.Email == request.Email, cancellationToken);
        if (emailInUse)
        {
            return Conflict(new { message = "A user with that email already exists." });
        }

        var maxUsers = await _db.Subscriptions
            .Where(s => s.CompanyId == companyId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.Plan!.MaxUsers)
            .FirstOrDefaultAsync(cancellationToken);

        if (maxUsers is int limit)
        {
            var currentUserCount = await _db.Users.CountAsync(
                u => u.CompanyId == companyId && u.Status != "suspended", cancellationToken);
            if (currentUserCount >= limit)
            {
                return BadRequest(new { message = $"Your plan allows up to {limit} users. Upgrade your tier to add more." });
            }
        }

        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            DepartmentId = request.DepartmentId,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            // Placeholder hash: nobody can log in with this until they set a real
            // password via the emailed invite link.
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
            Role = request.Role,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        var result = await _passwordResetService.IssueAndSendAsync(_db, user, isNewAccount: true, cancellationToken);

        return CreatedAtAction(nameof(GetUsers), new { id = user.Id }, new
        {
            user.Id,
            user.Email,
            user.Role,
            emailSent = result.EmailSent,
            manualLink = result.EmailSent ? null : result.Link
        });
    }

    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken cancellationToken)
    {
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole is not ("owner" or "admin"))
        {
            return Forbid();
        }

        var companyId = User.GetCompanyId();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.CompanyId == companyId, cancellationToken);
        if (user is null) return NotFound();

        var result = await _passwordResetService.IssueAndSendAsync(_db, user, isNewAccount: false, cancellationToken);

        return Ok(new
        {
            message = result.EmailSent
                ? $"A password reset link has been emailed to {user.Email}."
                : $"Email failed to send to {user.Email} — copy the link below and send it manually.",
            manualLink = result.EmailSent ? null : result.Link
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeactivateUser(Guid id, CancellationToken cancellationToken)
    {
        var callerRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (callerRole is not ("owner" or "admin"))
        {
            return Forbid();
        }

        var companyId = User.GetCompanyId();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.CompanyId == companyId, cancellationToken);
        if (user is null) return NotFound();

        if (user.Role == "owner")
        {
            var otherOwners = await _db.Users.CountAsync(
                u => u.CompanyId == companyId && u.Role == "owner" && u.Id != id, cancellationToken);
            if (otherOwners == 0)
            {
                return BadRequest(new { message = "Cannot remove the last owner of a company." });
            }
        }

        user.Status = "suspended";
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}
