using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace VoxLink.Api.Storage;

public class SupabaseStorageClient
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseStorageOptions _options;
    private bool _bucketEnsured;

    public SupabaseStorageClient(HttpClient httpClient, IOptions<SupabaseStorageOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = new Uri($"{_options.Url}/storage/v1/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
        _httpClient.DefaultRequestHeaders.Add("apikey", _options.ServiceRoleKey);
    }

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        if (_bucketEnsured) return;

        var response = await _httpClient.PostAsJsonAsync("bucket", new
        {
            id = _options.Bucket,
            name = _options.Bucket,
            @public = false
        }, cancellationToken);

        // 400 here means the bucket already exists — fine, anything else is a real problem.
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.BadRequest)
        {
            response.EnsureSuccessStatusCode();
        }

        _bucketEnsured = true;
    }

    public async Task UploadAsync(string path, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);

        using var byteContent = new ByteArrayContent(content);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var request = new HttpRequestMessage(HttpMethod.Post, $"object/{_options.Bucket}/{path}")
        {
            Content = byteContent
        };
        request.Headers.Add("x-upsert", "true");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> GetSignedUrlAsync(string path, int expiresInSeconds, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"object/sign/{_options.Bucket}/{path}", new { expiresIn = expiresInSeconds }, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var signedPath = json.GetProperty("signedURL").GetString();
        return $"{_options.Url}/storage/v1{signedPath}";
    }
}
