namespace VoxLink.Api.Services;

public class TwilioVoiceOptions
{
    public const string SectionName = "TwilioVoice";

    // Standard (non-restricted) API key used to sign Voice SDK access tokens —
    // separate from the AccountSid/AuthToken used for the REST Calls API.
    public string ApiKeySid { get; set; } = "";
    public string ApiKeySecret { get; set; } = "";

    // Set once the TwiML Application exists (needs a public Voice URL first).
    public string TwiMLAppSid { get; set; } = "";
}
