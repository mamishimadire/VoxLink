using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VoxLink.Api.Auditing;
using VoxLink.Api.Auth;
using VoxLink.Api.Billing;
using VoxLink.Api.Data;
using VoxLink.Api.Models;
using VoxLink.Api.Pdf;
using VoxLink.Api.Services;

namespace VoxLink.Api.Controllers;

public record DialRequest(string To);

public record RecentCallResponse(
    Guid Id, string DestinationNumber, string Direction, string Status,
    DateTimeOffset? StartedAt, int DurationSeconds, bool IsFavorite);

public record SetCallFavoriteRequest(bool IsFavorite);

[ApiController]
[Authorize]
[Route("api/calls")]
public class CallsController : ControllerBase
{
    private readonly ITwilioClient _twilioClient;
    private readonly VoxLinkDbContext _db;
    private readonly VoxLinkServiceDbContext _serviceDb;
    private readonly VoiceTokenService _voiceTokenService;
    private readonly TwilioOptions _twilioOptions;
    private readonly BackendOptions _backendOptions;
    private readonly BillingOptions _billingOptions;

    public CallsController(
        ITwilioClient twilioClient, VoxLinkDbContext db, VoxLinkServiceDbContext serviceDb,
        VoiceTokenService voiceTokenService, IOptions<TwilioOptions> twilioOptions, IOptions<BackendOptions> backendOptions,
        IOptions<BillingOptions> billingOptions)
    {
        _twilioClient = twilioClient;
        _db = db;
        _serviceDb = serviceDb;
        _voiceTokenService = voiceTokenService;
        _twilioOptions = twilioOptions.Value;
        _backendOptions = backendOptions.Value;
        _billingOptions = billingOptions.Value;
    }

    [HttpPost("dial")]
    public async Task<IActionResult> Dial([FromBody] DialRequest request, CancellationToken cancellationToken)
    {
        var to = CallClassifier.NormalizeNumber(request.To, _billingOptions.LocalCountryCode);
        var result = await _twilioClient.DialAsync(to, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var callId = Guid.NewGuid();
        _db.Calls.Add(new Call
        {
            Id = callId,
            CompanyId = User.GetCompanyId(),
            UserId = User.GetUserId(),
            DestinationNumber = to,
            Direction = "outbound",
            Status = MapTwilioStatus(result.Status),
            ProviderCallId = result.Sid,
            StartedAt = now,
            CreatedAt = now
        });
        AuditLogService.Log(_db, User.GetCompanyId(), User.GetUserId(), User.GetEmail(), "call.placed", "call", callId,
            $"Called {to}");
        await _db.SaveChangesAsync(cancellationToken);

        return Content(result.RawJson, "application/json");
    }

    [HttpGet("recent")]
    public async Task<IActionResult> GetRecent(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var calls = await _db.Calls
            .Where(c => c.UserId == userId && c.DeletedAt == null)
            .OrderByDescending(c => c.IsFavorite).ThenByDescending(c => c.StartedAt)
            .Take(50)
            .Select(c => new RecentCallResponse(c.Id, c.DestinationNumber, c.Direction, c.Status, c.StartedAt, c.DurationSeconds, c.IsFavorite))
            .ToListAsync(cancellationToken);

        return Ok(calls);
    }

    [HttpPut("{id:guid}/favorite")]
    public async Task<IActionResult> SetFavorite(Guid id, SetCallFavoriteRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var call = await _db.Calls.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);
        if (call is null) return NotFound();

        call.IsFavorite = request.IsFavorite;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteCall(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var call = await _db.Calls.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);
        if (call is null) return NotFound();

        // Soft delete: hides it from this user's history without touching
        // the usage data automatic billing already relied on, or will.
        call.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// The softphone (browser) calls this to get a token for the Twilio
    /// Voice SDK, identifying itself as the current user so /voice below
    /// knows who's placing the call.
    /// </summary>
    [HttpGet("voice-token")]
    public IActionResult GetVoiceToken()
    {
        var token = _voiceTokenService.GenerateToken(User.GetUserId().ToString());
        return Ok(new { token });
    }

    /// <summary>
    /// Twilio requests this (anonymously — the browser's Voice SDK connection
    /// carries no HTTP session) whenever the softphone places an outbound
    /// call, to learn what to actually do with it.
    /// </summary>
    [HttpPost("voice")]
    [AllowAnonymous]
    public async Task<IActionResult> Voice([FromForm] IFormCollection payload, CancellationToken cancellationToken)
    {
        var to = CallClassifier.NormalizeNumber(payload["To"].ToString(), _billingOptions.LocalCountryCode);
        var from = payload["From"].ToString();
        var callSid = payload["CallSid"].ToString();
        var identity = from.StartsWith("client:", StringComparison.Ordinal) ? from["client:".Length..] : null;

        if (Guid.TryParse(identity, out var userId) && !string.IsNullOrEmpty(to))
        {
            var user = await _serviceDb.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is not null)
            {
                var now = DateTimeOffset.UtcNow;
                var callId = Guid.NewGuid();
                _serviceDb.Calls.Add(new Call
                {
                    Id = callId,
                    CompanyId = user.CompanyId,
                    UserId = user.Id,
                    DestinationNumber = to,
                    Direction = "outbound",
                    Status = "initiated",
                    ProviderCallId = callSid,
                    StartedAt = now,
                    CreatedAt = now
                });
                AuditLogService.Log(_serviceDb, user.CompanyId, user.Id, user.Email, "call.placed", "call", callId,
                    $"Called {to}");
                await _serviceDb.SaveChangesAsync(cancellationToken);
            }
        }

        var actionUrl = $"{_backendOptions.PublicBaseUrl}/api/calls/webhooks/dial-complete";
        var twiml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Response>
              <Dial callerId="{_twilioOptions.PhoneNumber}" action="{actionUrl}" method="POST">
                <Number>{System.Security.SecurityElement.Escape(to)}</Number>
              </Dial>
            </Response>
            """;

        return Content(twiml, "application/xml");
    }

    /// <summary>
    /// Fires once when a softphone-originated call ends — reports the final
    /// outcome for the ORIGINAL (browser-leg) CallSid, not a new child call.
    /// </summary>
    [HttpPost("webhooks/dial-complete")]
    [AllowAnonymous]
    public async Task<IActionResult> DialComplete([FromForm] IFormCollection payload, CancellationToken cancellationToken)
    {
        var callSid = payload["CallSid"].ToString();
        var dialCallStatus = payload["DialCallStatus"].ToString();
        if (string.IsNullOrEmpty(callSid)) return Content("<Response/>", "application/xml");

        var call = await _serviceDb.Calls.FirstOrDefaultAsync(c => c.ProviderCallId == callSid, cancellationToken);
        if (call is not null)
        {
            call.Status = MapTwilioStatus(dialCallStatus);
            call.EndedAt = DateTimeOffset.UtcNow;
            if (dialCallStatus == "answered" || dialCallStatus == "completed")
            {
                call.AnsweredAt ??= call.StartedAt;
            }
            if (int.TryParse(payload["DialCallDuration"].ToString(), out var duration))
            {
                call.DurationSeconds = duration;
            }
            await _serviceDb.SaveChangesAsync(cancellationToken);
        }

        return Content("<Response/>", "application/xml");
    }

    [HttpPost("webhooks/twilio")]
    [AllowAnonymous]
    public async Task<IActionResult> TwilioWebhook([FromForm] IFormCollection payload, CancellationToken cancellationToken)
    {
        var callSid = payload["CallSid"].ToString();
        var callStatus = payload["CallStatus"].ToString();
        if (string.IsNullOrEmpty(callSid)) return Ok();

        // Anonymous request from Twilio — no session/company context to
        // scope by, so this uses the service context (bypasses RLS) and
        // looks the call up by its provider ID instead.
        var call = await _serviceDb.Calls.FirstOrDefaultAsync(c => c.ProviderCallId == callSid, cancellationToken);
        if (call is null) return Ok();

        call.Status = MapTwilioStatus(callStatus);

        if (callStatus == "in-progress" && call.AnsweredAt is null)
        {
            call.AnsweredAt = DateTimeOffset.UtcNow;
        }

        if (callStatus is "completed" or "busy" or "failed" or "no-answer" or "canceled")
        {
            call.EndedAt = DateTimeOffset.UtcNow;
            if (int.TryParse(payload["CallDuration"].ToString(), out var duration))
            {
                call.DurationSeconds = duration;
            }
        }

        await _serviceDb.SaveChangesAsync(cancellationToken);
        return Ok(new { received = true });
    }

    private static string MapTwilioStatus(string twilioStatus) => twilioStatus switch
    {
        "queued" => "initiated",
        "ringing" => "ringing",
        "in-progress" or "answered" or "completed" => twilioStatus == "completed" ? "completed" : "answered",
        "busy" => "busy",
        "failed" => "failed",
        "no-answer" => "no_answer",
        "canceled" => "failed",
        _ => "initiated"
    };
}
