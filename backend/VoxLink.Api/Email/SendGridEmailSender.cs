using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace VoxLink.Api.Email;

public class SendGridEmailSender : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly SendGridOptions _options;

    public SendGridEmailSender(HttpClient httpClient, IOptions<SendGridOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = new Uri("https://api.sendgrid.com/v3/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public async Task SendAsync(
        string toEmail, string subject, string htmlBody, CancellationToken cancellationToken,
        IReadOnlyList<EmailAttachment>? attachments = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["personalizations"] = new[] { new { to = new[] { new { email = toEmail } } } },
            ["from"] = new { email = _options.FromEmail, name = _options.FromName },
            ["subject"] = subject,
            ["content"] = new[] { new { type = "text/html", value = htmlBody } }
        };

        if (attachments is { Count: > 0 })
        {
            payload["attachments"] = attachments
                .Select(a => new { content = Convert.ToBase64String(a.Content), filename = a.FileName, type = a.ContentType })
                .ToList();
        }

        var response = await _httpClient.PostAsJsonAsync("mail/send", payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"SendGrid send failed ({(int)response.StatusCode}): {body}");
        }
    }
}
