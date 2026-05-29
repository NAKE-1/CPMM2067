using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CPMM2067.Core;
using CPMM2067.Core.Mods;
using Microsoft.Extensions.Logging;

namespace CPMM2067.Frameworks;

public enum InstallEntryStatus { Installed, Reverted, DryRun, Failed, Downloaded }

public sealed record InstallEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public string Name { get; init; } = string.Empty;
    public string? Version { get; init; }
    public ModFramework Framework { get; init; }
    public string SourceArchivePath { get; init; } = string.Empty;
    public string? SourceArchiveSha256 { get; init; }
    public List<string> RelativePaths { get; init; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public InstallEntryStatus Status { get; init; } = InstallEntryStatus.Installed;

    public string? Notes { get; init; }
    public List<string> DependenciesDetected { get; init; } = new();
}

public sealed class InstallJournal
{
    private readonly ILogger<InstallJournal> _log;
    private static readonly JsonSerializerOptions s_opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public InstallJournal(ILogger<InstallJournal> log)
    {
        _log = log;
        Directory.CreateDirectory(JournalDir);
    }

    public static string JournalDir => Path.Combine(AppPaths.AppData, "journal");

    public async Task SaveAsync(InstallEntry entry, CancellationToken ct = default)
    {
        Directory.CreateDirectory(JournalDir);
        var path = Path.Combine(JournalDir, $"{entry.At:yyyyMMdd-HHmmss}-{entry.Id}.json");
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, entry, s_opts, ct).ConfigureAwait(false);
        _log.LogInformation("Journaled install {Name} ({Status}) -> {Path}", entry.Name, entry.Status, path);
    }

    public IReadOnlyList<(string Path, InstallEntry Entry)> LoadAll()
    {
        if (!Directory.Exists(JournalDir)) return Array.Empty<(string, InstallEntry)>();
        var results = new List<(string, InstallEntry)>();
        foreach (var file in Directory.EnumerateFiles(JournalDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var entry = JsonSerializer.Deserialize<InstallEntry>(json, s_opts);
                if (entry != null) results.Add((file, entry));
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Skipping corrupt journal {Path}", file);
            }
        }
        return results.OrderByDescending(r => r.Item2.At).ToList();
    }

    public async Task UpdateStatusAsync(string journalPath, InstallEntryStatus newStatus, string? notes = null, CancellationToken ct = default)
    {
        if (!File.Exists(journalPath)) return;
        var json = await File.ReadAllTextAsync(journalPath, ct).ConfigureAwait(false);
        var entry = JsonSerializer.Deserialize<InstallEntry>(json, s_opts);
        if (entry == null) return;
        var updated = entry with { Status = newStatus, Notes = notes ?? entry.Notes };
        await using var fs = File.Create(journalPath);
        await JsonSerializer.SerializeAsync(fs, updated, s_opts, ct).ConfigureAwait(false);
    }
}
