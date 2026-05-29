using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CPMM2067.Core.Compat;
using CPMM2067.Core.Game;
using CPMM2067.Core.Mods;
using Microsoft.Extensions.Logging;

namespace CPMM2067.Compat;

public sealed class CompatEngine : IDisposable
{
    private const string DefaultDbUrl = "https://raw.githubusercontent.com/cpmm2067/compat-db/main/db.json";
    private readonly HttpClient _http = new();
    private readonly ILogger<CompatEngine> _log;
    private CrowdCompatDb? _cache;

    public CompatEngine(ILogger<CompatEngine> log)
    {
        _log = log;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CPMM2067/0.1");
    }

    public async Task<CompatVerdict> EvaluateAsync(
        ModManifest manifest,
        GameInstallation game,
        CancellationToken ct = default)
    {
        var reasons = new List<string>();
        var status = CompatStatus.Unknown;

        var crowd = await GetDbAsync(ct).ConfigureAwait(false);
        if (crowd != null && manifest.NexusModId is int nm
            && crowd.Mods.TryGetValue(nm.ToString(), out var crowdEntry))
        {
            reasons.Add($"Crowd: {crowdEntry.Status} for game {crowdEntry.GameVersion} ({crowdEntry.Notes ?? "no notes"})");
            status = ParseCrowd(crowdEntry.Status, game.Version, crowdEntry.GameVersion);
        }

        if (!string.IsNullOrWhiteSpace(manifest.SupportedGameVersion))
        {
            reasons.Add($"Mod declares supportedGameVersion = {manifest.SupportedGameVersion}");
            if (GameVersion.TryParse(manifest.SupportedGameVersion, out var sup))
            {
                if (game.Version.Major == sup.Major && game.Version.Minor == sup.Minor)
                {
                    status = Worse(status, CompatStatus.Compatible);
                }
                else if (game.Version.Major == sup.Major)
                {
                    status = Worse(status, CompatStatus.Risky);
                    reasons.Add($"Minor version mismatch (mod {sup} vs game {game.Version})");
                }
                else
                {
                    status = Worse(status, CompatStatus.Incompatible);
                    reasons.Add($"Major version mismatch (mod {sup} vs game {game.Version})");
                }
            }
        }
        else
        {
            reasons.Add("Mod did not declare a supportedGameVersion");
            if (status == CompatStatus.Unknown) status = CompatStatus.Risky;
        }

        var headline = status switch
        {
            CompatStatus.Compatible => $"Looks compatible with game v{game.Version}",
            CompatStatus.Risky => "May or may not work — check the mod page",
            CompatStatus.Incompatible => $"Not expected to work on v{game.Version}",
            _ => "Compatibility unknown",
        };

        return new CompatVerdict(status, headline, reasons);
    }

    public async Task<CrowdCompatDb?> GetDbAsync(CancellationToken ct = default)
    {
        if (_cache != null) return _cache;
        try
        {
            _cache = await _http.GetFromJsonAsync<CrowdCompatDb>(DefaultDbUrl, ct).ConfigureAwait(false);
            _log.LogInformation("Loaded crowd compat DB with {Count} entries", _cache?.Mods.Count ?? 0);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Failed to fetch crowd compat DB; continuing without it");
        }
        return _cache;
    }

    private static CompatStatus Worse(CompatStatus a, CompatStatus b) => (CompatStatus)Math.Max((int)a, (int)b);

    private static CompatStatus ParseCrowd(string? status, GameVersion gameVer, string? entryGameVer)
    {
        if (entryGameVer != null && GameVersion.TryParse(entryGameVer, out var ev)
            && (ev.Major != gameVer.Major || ev.Minor != gameVer.Minor))
        {
            return CompatStatus.Risky;
        }
        return status?.ToLowerInvariant() switch
        {
            "compatible" or "works" or "good" => CompatStatus.Compatible,
            "risky" or "partial" => CompatStatus.Risky,
            "broken" or "incompatible" => CompatStatus.Incompatible,
            _ => CompatStatus.Unknown,
        };
    }

    public void Dispose() => _http.Dispose();
}

public sealed record CrowdCompatDb
{
    [JsonPropertyName("mods")] public Dictionary<string, CrowdCompatEntry> Mods { get; init; } = new();
}

public sealed record CrowdCompatEntry(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("gameVersion")] string? GameVersion,
    [property: JsonPropertyName("notes")] string? Notes);
