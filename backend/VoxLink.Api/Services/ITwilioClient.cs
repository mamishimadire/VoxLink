namespace VoxLink.Api.Services;

public record TwilioCallResult(string Sid, string Status, string RawJson);

public interface ITwilioClient
{
    Task<TwilioCallResult> DialAsync(string to, CancellationToken cancellationToken);
}
