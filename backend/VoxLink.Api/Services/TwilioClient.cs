using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace VoxLink.Api.Services;

public class TwilioClient : ITwilioClient
{
    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly BackendOptions _backendOptions;

    public TwilioClient(HttpClient httpClient, IOptions<TwilioOptions> options, IOptions<BackendOptions> backendOptions)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _backendOptions = backendOptions.Value;
        _httpClient.BaseAddress = new Uri("https://api.twilio.com/2010-04-01/");

        var (username, password) = string.IsNullOrEmpty(_options.ApiKeySid)
            ? (_options.AccountSid, _options.AuthToken)
            : (_options.ApiKeySid, _options.ApiKeySecret);

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<TwilioCallResult> DialAsync(string to, CancellationToken cancellationToken)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _options.PhoneNumber),
            new("Url", _options.TwimlUrl)
        };

        if (!string.IsNullOrEmpty(_backendOptions.PublicBaseUrl))
        {
            fields.Add(new("StatusCallback", $"{_backendOptions.PublicBaseUrl}/api/calls/webhooks/twilio"));
            foreach (var evt in new[] { "initiated", "ringing", "answered", "completed" })
            {
                fields.Add(new("StatusCallbackEvent", evt));
            }
            fields.Add(new("StatusCallbackMethod", "POST"));
        }

        var response = await _httpClient.PostAsync(
            $"Accounts/{_options.AccountSid}/Calls.json", new FormUrlEncodedContent(fields), cancellationToken);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        var json = JsonDocument.Parse(raw).RootElement;
        return new TwilioCallResult(json.GetProperty("sid").GetString()!, json.GetProperty("status").GetString()!, raw);
    }
}
