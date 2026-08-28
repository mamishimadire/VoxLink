using Microsoft.Extensions.Options;
using Twilio.Jwt.AccessToken;

namespace VoxLink.Api.Services;

public class VoiceTokenService
{
    private readonly TwilioOptions _twilioOptions;
    private readonly TwilioVoiceOptions _voiceOptions;

    public VoiceTokenService(IOptions<TwilioOptions> twilioOptions, IOptions<TwilioVoiceOptions> voiceOptions)
    {
        _twilioOptions = twilioOptions.Value;
        _voiceOptions = voiceOptions.Value;
    }

    /// <summary>
    /// Builds a short-lived token the Twilio Voice SDK uses in the browser to
    /// register as "identity" and place/receive calls. The identity becomes
    /// the "From" the /voice TwiML endpoint sees (as "client:{identity}"),
    /// which is how we know which VoxLink user placed a given call.
    /// </summary>
    public string GenerateToken(string identity)
    {
        var grant = new VoiceGrant
        {
            OutgoingApplicationSid = _voiceOptions.TwiMLAppSid,
            IncomingAllow = true
        };

        var token = new Token(
            _twilioOptions.AccountSid,
            _voiceOptions.ApiKeySid,
            _voiceOptions.ApiKeySecret,
            identity: identity,
            grants: [grant],
            expiration: DateTime.UtcNow.AddHours(1));

        return token.ToJwt();
    }
}
