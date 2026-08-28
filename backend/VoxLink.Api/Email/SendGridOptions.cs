namespace VoxLink.Api.Email;

public class SendGridOptions
{
    public const string SectionName = "SendGrid";

    public string ApiKey { get; set; } = "";

    // Must be a verified sender in SendGrid (Settings -> Sender Authentication)
    // — either a single verified email or an address on an authenticated domain.
    public string FromEmail { get; set; } = "";
    public string FromName { get; set; } = "VoxLink";
}
