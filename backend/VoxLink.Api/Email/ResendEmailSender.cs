using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace VoxLink.Api.Email;

public class ResendEmailSender : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly ResendOptions _options;

    public ResendEmailSender(HttpClient httpClient, IOptions<ResendOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = new Uri("https://api.resend.com/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public async Task SendAsync(
        string toEmail, string subject, string htmlBody, CancellationToken cancellationToken,
        IReadOnlyList<EmailAttachment>? attachments = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["from"] = _options.FromEmail,
            ["to"] = new[] { toEmail },
            ["subject"] = subject,
            ["html"] = htmlBody
        };

        if (attachments is { Count: > 0 })
        {
            payload["attachments"] = attachments
                .Select(a => new { filename = a.FileName, content = Convert.ToBase64String(a.Content) })
                .ToList();
        }

        var response = await _httpClient.PostAsJsonAsync("emails", payload, cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
