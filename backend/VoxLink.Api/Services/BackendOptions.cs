namespace VoxLink.Api.Services;

public class BackendOptions
{
    public const string SectionName = "Backend";

    // Must be a publicly reachable URL (e.g. via ngrok in dev) for Twilio to
    // deliver call status callbacks. Leave blank to skip status callbacks —
    // calls are still logged, just without live status/duration updates.
    public string PublicBaseUrl { get; set; } = "";
}
