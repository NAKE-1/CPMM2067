using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CPMM2067.Nexus;

public sealed class NexusApiClient : IDisposable
{
    public const string GameDomain = "cyberpunk2077";
    private const string BaseUrl = "https://api.nexusmods.com/v1/";

    private readonly HttpClient _http;
    private readonly ILogger<NexusApiClient> _log;
    private readonly NexusRateLimitTracker? _tracker;
    private string? _apiKey;

    public NexusApiClient(ILogger<NexusApiClient> log, NexusRateLimitTracker? tracker = null)
    {
        _log = log;
        _tracker = tracker;
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CPMM2067/0.1 (+https://github.com/cpmm2067)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    private async Task<HttpResponseMessage> SendAndTrackAsync(string relUrl, CancellationToken ct)
    {
        var resp = await _http.GetAsync(relUrl, ct).ConfigureAwait(false);
        _tracker?.RecordResponse(resp.Headers);
        return resp;
    }

    public void SetApiKey(string apiKey)
    {
        _apiKey = apiKey;
        if (_http.DefaultRequestHeaders.Contains("apikey")) _http.DefaultRequestHeaders.Remove("apikey");
        _http.DefaultRequestHeaders.Add("apikey", apiKey);
    }

    public async Task<NexusUserInfo?> ValidateKeyAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey)) throw new InvalidOperationException("API key not set");
        var resp = await SendAndTrackAsync("users/validate.json", ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("Nexus validate failed: {Status}", resp.StatusCode);
            return null;
        }
        return await resp.Content.ReadFromJsonAsync<NexusUserInfo>(cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<NexusMod?> GetModAsync(int modId, CancellationToken ct = default)
    {
        var resp = await SendAndTrackAsync($"games/{GameDomain}/mods/{modId}.json", ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<NexusMod>(cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<NexusFileList?> GetFilesAsync(int modId, CancellationToken ct = default)
    {
        var resp = await SendAndTrackAsync($"games/{GameDomain}/mods/{modId}/files.json", ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<NexusFileList>(cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<NexusDownloadLink[]?> GetDownloadLinksAsync(
        int modId, int fileId, string? key = null, long expires = 0, CancellationToken ct = default)
    {
        var qs = (key, expires) is (not null, > 0)
            ? $"?key={Uri.EscapeDataString(key!)}&expires={expires}"
            : string.Empty;
        var resp = await SendAndTrackAsync(
            $"games/{GameDomain}/mods/{modId}/files/{fileId}/download_link.json{qs}", ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<NexusDownloadLink[]>(cancellationToken: ct).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();
}

public sealed record NexusUserInfo(
    [property: JsonPropertyName("user_id")] int UserId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("is_premium")] bool IsPremium,
    [property: JsonPropertyName("email")] string? Email);

public sealed record NexusMod(
    [property: JsonPropertyName("mod_id")] int ModId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("author")] string? Author,
    [property: JsonPropertyName("category_id")] int? CategoryId,
    [property: JsonPropertyName("updated_timestamp")] long UpdatedTimestamp,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("picture_url")] string? PictureUrl);

public sealed record NexusFileList(
    [property: JsonPropertyName("files")] List<NexusFile> Files);

public sealed record NexusFile(
    [property: JsonPropertyName("file_id")] int FileId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("category_id")] int CategoryId,
    [property: JsonPropertyName("category_name")] string? CategoryName,
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("uploaded_timestamp")] long UploadedTimestamp);

public sealed record NexusDownloadLink(
    [property: JsonPropertyName("name")] string ServerName,
    [property: JsonPropertyName("short_name")] string ShortName,
    [property: JsonPropertyName("URI")] string Uri);
