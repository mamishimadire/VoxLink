using Microsoft.EntityFrameworkCore;
using VoxLink.Api.Data;
using VoxLink.Api.Email;
using VoxLink.Api.Models;

namespace VoxLink.Api.Billing;

/// <summary>
/// Emails VoxLink's own owner/admin users a copy of a client's signed
/// agreement PDF. The signing request comes from a client-company user, so
/// it can't see VoxLink's own internal company/users through the
/// tenant-scoped context (RLS would block it) — this uses the elevated
/// service context for that one cross-tenant lookup, same as the invoice
/// generation job does.
/// </summary>
public class AgreementNotificationService
{
    private readonly VoxLinkServiceDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<AgreementNotificationService> _logger;

    public AgreementNotificationService(VoxLinkServiceDbContext db, IEmailSender emailSender, ILogger<AgreementNotificationService> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task NotifyAsync(
        Company company, string signedByName, string signedByEmail, DateTimeOffset signedAt, byte[] pdfBytes, CancellationToken cancellationToken)
    {
        var recipients = await _db.Users
            .Where(u => u.Company!.IsInternal && (u.Role == "owner" || u.Role == "admin") && u.Status != "suspended")
            .Select(u => u.Email)
            .ToListAsync(cancellationToken);

        if (recipients.Count == 0) return;

        var html = $"""
            <p>{company.Name} signed the pay-as-you-go services agreement.</p>
            <p>Signed by {signedByName} ({signedByEmail}) on {signedAt:yyyy-MM-dd HH:mm} UTC.</p>
            <p>A copy is attached. Every signed agreement is also available to download from the Agreements tab.</p>
            """;
        var attachment = new EmailAttachment($"{company.Name}-agreement.pdf", pdfBytes, "application/pdf");

        foreach (var recipient in recipients)
        {
            try
            {
                await _emailSender.SendAsync(recipient, $"Agreement signed — {company.Name}", html, cancellationToken, [attachment]);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to email signed agreement notice to {Recipient}", recipient);
            }
        }
    }
}
