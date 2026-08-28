namespace VoxLink.Api.Email;

public record EmailAttachment(string FileName, byte[] Content, string ContentType);

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken, IReadOnlyList<EmailAttachment>? attachments = null);
}
