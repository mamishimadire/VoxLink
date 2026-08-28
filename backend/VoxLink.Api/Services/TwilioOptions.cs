namespace VoxLink.Api.Services;

public class TwilioOptions
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";

    // Preferred over AuthToken: a restricted API Key (SID starts with "SK") + its Secret.
    // When set, these are used for Basic Auth instead of AccountSid/AuthToken.
    public string ApiKeySid { get; set; } = "";
    public string ApiKeySecret { get; set; } = "";

    public string PhoneNumber { get; set; } = "";

    // TwiML that Twilio fetches once the call connects, to know what to say/do.
    // Defaults to Twilio's public demo TwiML so a call can be tested before we host our own.
    public string TwimlUrl { get; set; } = "http://demo.twilio.com/docs/voice.xml";
}
