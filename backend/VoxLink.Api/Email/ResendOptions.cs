namespace VoxLink.Api.Email;

public class ResendOptions
{
    public const string SectionName = "Resend";

    public string ApiKey { get; set; } = "";

    // Resend's shared sandbox sender works with no domain verification, for early testing.
    // Swap to a verified address on your own domain (e.g. noreply@voxlink.co.za) before going live.
    public string FromEmail { get; set; } = "VoxLink <onboarding@resend.dev>";
}
