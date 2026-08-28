using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VoxLink.Api.Data;
using VoxLink.Api.Email;
using VoxLink.Api.Models;

namespace VoxLink.Api.Auth;

public record ResetLinkResult(bool EmailSent, string Link);

public class PasswordResetService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    private readonly IEmailSender _emailSender;
    private readonly FrontendOptions _frontendOptions;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        IEmailSender emailSender, IOptions<FrontendOptions> frontendOptions, ILogger<PasswordResetService> logger)
    {
        _emailSender = emailSender;
        _frontendOptions = frontendOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Issues a one-time token and emails a "set your password" (new account) or
    /// "reset your password" (existing account) link. Nothing sensitive is ever
    /// returned to an unauthenticated caller — only the token's hash is stored.
    ///
    /// The token is always issued even if the email fails to send (e.g. email
    /// provider not configured yet, or recipient blocked by a sandbox
    /// restriction) — callers should treat that as a soft failure and, since
    /// they're already an admin authorized to manage this user, may relay the
    /// returned link to them manually through another channel.
    ///
    /// Callers pass the DbContext to save through: the tenant-scoped one when
    /// acting on a user in their own (or, for platform admins, any) company,
    /// or the service context for pre-auth flows (forgot-password) that have
    /// no company context yet.
    /// </summary>
    public async Task<ResetLinkResult> IssueAndSendAsync(DbContext db, User user, bool isNewAccount, CancellationToken cancellationToken)
    {
        var rawToken = PasswordResetTokenService.GenerateRawToken();
        user.PasswordResetTokenHash = PasswordResetTokenService.Hash(rawToken);
        user.PasswordResetExpiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime);
        await db.SaveChangesAsync(cancellationToken);

        var link = $"{_frontendOptions.BaseUrl}/reset-password?token={rawToken}";

        var subject = isNewAccount ? "Welcome to VoxLink — set your password" : "Reset your VoxLink password";
        var html = isNewAccount
            ? EmailTemplates.SetPassword(user.FirstName, link)
            : EmailTemplates.ResetPassword(user.FirstName, link);

        try
        {
            await _emailSender.SendAsync(user.Email, subject, html, cancellationToken);
            return new ResetLinkResult(true, link);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send invite/reset email to {Email}", user.Email);
            return new ResetLinkResult(false, link);
        }
    }
}
