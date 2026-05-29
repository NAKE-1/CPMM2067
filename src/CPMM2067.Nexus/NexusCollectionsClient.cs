using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CPMM2067.Nexus;

public sealed record NexusCollectionFetchResult(
    bool Ok,
    NexusCollectionResult? Collection,
    string? ErrorMessage,
    string? RawResponseBody);

/// <summary>
/// Nexus v2 GraphQL client (Collections endpoint).
///
/// AUTH: Nexus's v2 GraphQL accepts EITHER:
///   - `apikey: <personal-API-key>` header — works for some queries, but typically NOT Collections
///   - `Authorization: Bearer <JWT>` — issued by the Nexus OAuth flow, required for full Collections access
///
/// We try Bearer first if a JWT is provided, otherwise fall back to apikey.
/// </summary>
public sealed class NexusCollectionsClient : IDisposable
{
    private const string GraphQlUrl = "https://api.nexusmods.com/v2/graphql";

    private readonly HttpClient _http;
    private readonly ILogger<NexusCollectionsClient> _log;
    private string? _apiKey;
    private string? _jwt;

    public NexusCollectionsClient(ILogger<NexusCollectionsClient> log)
    {
        _log = log;
        _http = new HttpClient { BaseAddress = new Uri(GraphQlUrl) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CPMM2067/0.1");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public void SetApiKey(string apiKey) => _apiKey = apiKey;
    public void SetJwt(string jwt) => _jwt = jwt;

    public async Task<NexusCollectionFetchResult> FetchAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey) && string.IsNullOrEmpty(_jwt))
            return new(false, null, "No API key OR JWT set. Set at least one in Settings.", null);

        slug = ExtractSlug(slug);

        var query = """
            query Collection($slug: String!) {
              collection(slug: $slug, viewAdultContent: true) {
                name
                summary
                slug
                game { domainName }
                user { name }
                latestPublishedRevision {
                  revisionNumber
                  modFiles {
                    optional
                    file {
                      fileId
                      name
                      mod {
                        modId
                        name
                        author
                      }
                    }
                  }
                }
              }
            }
            """;

        var body = new { query, variables = new { slug } };

        // Build the request fresh so we can swap headers per attempt.
        async Task<(System.Net.HttpStatusCode Status, string Body)> SendAsync(bool useBearer)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "");
            req.Content = JsonContent.Create(body);
            if (useBearer && !string.IsNullOrEmpty(_jwt))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwt);
            else if (!string.IsNullOrEmpty(_apiKey))
                req.Headers.TryAddWithoutValidation("apikey", _apiKey);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (resp.StatusCode, text);
        }

        // Attempt 1: Bearer if JWT present
        (System.Net.HttpStatusCode status, string raw) first = (0, "");
        if (!string.IsNullOrEmpty(_jwt))
            first = await SendAsync(useBearer: true);

        // Attempt 2 (or only attempt): apikey
        var apiResult = (status: (System.Net.HttpStatusCode)0, raw: "");
        if ((first.status == 0 || (int)first.status >= 400) && !string.IsNullOrEmpty(_apiKey))
            apiResult = await SendAsync(useBearer: false);

        // Pick the more successful response
        var best = (int)first.status is >= 200 and < 300 ? first : apiResult;
        if ((int)best.status is 0 or >= 400)
        {
            var msg = first.status == 0
                ? $"HTTP {(int)apiResult.status} {apiResult.status} from Nexus (apikey)"
                : $"HTTP {(int)first.status} {first.status} from Nexus (Bearer JWT)";
            return new(false, null, msg, first.raw.Length > 0 ? first.raw : apiResult.raw);
        }

        try
        {
            var doc = JsonSerializer.Deserialize<GraphQlResponse>(best.raw);
            if (doc?.Errors is { Count: > 0 } errs)
            {
                var joined = string.Join("; ", errs.ConvertAll(e => e.Message ?? "unknown"));
                return new(false, null, "GraphQL errors: " + joined, best.raw);
            }
            if (doc?.Data?.Collection == null)
                return new(false, null, "Nexus returned no collection data (slug not found or not visible to this auth).", best.raw);

            var c = doc.Data.Collection;
            var rev = c.LatestPublishedRevision;
            var gameDomain = c.Game?.DomainName ?? "cyberpunk2077";
            var mods = new List<NexusCollectionModEntry>();
            if (rev?.ModFiles != null)
            {
                foreach (var mf in rev.ModFiles)
                {
                    if (mf.File?.Mod == null) continue;
                    mods.Add(new NexusCollectionModEntry(
                        ModId: mf.File.Mod.ModId,
                        FileId: mf.File.FileId,
                        ModName: mf.File.Mod.Name ?? $"mod_{mf.File.Mod.ModId}",
                        FileName: mf.File.Name ?? $"file_{mf.File.FileId}",
                        Author: mf.File.Mod.Author ?? "",
                        GameDomain: gameDomain,
                        Optional: mf.Optional));
                }
            }

            return new(true,
                new NexusCollectionResult(
                    Slug: c.Slug ?? slug,
                    Name: c.Name ?? slug,
                    Summary: c.Summary ?? "",
                    Author: c.User?.Name ?? "",
                    Revision: rev?.RevisionNumber ?? 0,
                    Mods: mods),
                null,
                best.raw);
        }
        catch (Exception ex)
        {
            return new(false, null, "Failed to parse Nexus response: " + ex.Message, best.raw);
        }
    }

    public static string ExtractSlug(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        if (Uri.TryCreate(input, UriKind.Absolute, out var u))
        {
            var seg = u.AbsolutePath.Trim('/').Split('/');
            for (var i = 0; i < seg.Length - 1; i++)
                if (string.Equals(seg[i], "collections", StringComparison.OrdinalIgnoreCase))
                    return seg[i + 1];
        }
        return input.Trim();
    }

    public void Dispose() => _http.Dispose();

    // --- DTOs -------------------------------------------------
    private sealed class GraphQlResponse
    {
        [JsonPropertyName("data")] public GraphQlData? Data { get; set; }
        [JsonPropertyName("errors")] public List<GqlError>? Errors { get; set; }
    }
    private sealed class GqlError
    {
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
    private sealed class GraphQlData
    {
        [JsonPropertyName("collection")] public CollectionDto? Collection { get; set; }
    }
    private sealed class CollectionDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("summary")] public string? Summary { get; set; }
        [JsonPropertyName("slug")] public string? Slug { get; set; }
        [JsonPropertyName("user")] public UserDto? User { get; set; }
        [JsonPropertyName("game")] public GameDto? Game { get; set; }
        [JsonPropertyName("latestPublishedRevision")] public RevisionDto? LatestPublishedRevision { get; set; }
    }
    private sealed class UserDto { [JsonPropertyName("name")] public string? Name { get; set; } }
    private sealed class GameDto { [JsonPropertyName("domainName")] public string? DomainName { get; set; } }
    private sealed class RevisionDto
    {
        [JsonPropertyName("revisionNumber")] public int RevisionNumber { get; set; }
        [JsonPropertyName("modFiles")] public List<ModFileDto>? ModFiles { get; set; }
    }
    private sealed class ModFileDto
    {
        [JsonPropertyName("optional")] public bool Optional { get; set; }
        [JsonPropertyName("file")] public FileDto? File { get; set; }
    }
    private sealed class FileDto
    {
        [JsonPropertyName("fileId")] public int FileId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("mod")] public ModDto? Mod { get; set; }
    }
    private sealed class ModDto
    {
        [JsonPropertyName("modId")] public int ModId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("author")] public string? Author { get; set; }
    }
}

public sealed record NexusCollectionResult(
    string Slug,
    string Name,
    string Summary,
    string Author,
    int Revision,
    IReadOnlyList<NexusCollectionModEntry> Mods);

public sealed record NexusCollectionModEntry(
    int ModId,
    int FileId,
    string ModName,
    string FileName,
    string Author,
    string GameDomain,
    bool Optional);
