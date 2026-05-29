using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CPMM2067.Core.Game;
using CPMM2067.Core.Mods;
using CPMM2067.Frameworks.RedMod;
using Microsoft.Extensions.Logging;

namespace CPMM2067.Frameworks;

public sealed record DiscoveredMod(
    ModFramework Framework,
    string Name,
    string? Version,
    string AbsolutePath,
    string RelativePath);

public sealed class ModScanner
{
    private readonly ILogger<ModScanner> _log;
    private static readonly JsonSerializerOptions s_jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public ModScanner(ILogger<ModScanner> log) => _log = log;

    public async Task<IReadOnlyList<DiscoveredMod>> ScanAsync(GameInstallation game, CancellationToken ct = default)
    {
        var results = new List<DiscoveredMod>();
        await Task.Run(() =>
        {
            ScanRedMod(game, results, ct);
            ScanLegacyArchive(game, results, ct);
            ScanRed4ext(game, results, ct);
            ScanCet(game, results, ct);
            ScanR6Tweaks(game, results, ct);
            ScanR6Scripts(game, results, ct);
        }, ct).ConfigureAwait(false);

        _log.LogInformation("Scan found {Count} mod(s) across all frameworks at {Path}", results.Count, game.InstallDir);
        return results;
    }

    private void ScanRedMod(GameInstallation game, List<DiscoveredMod> sink, CancellationToken ct)
    {
        if (!Directory.Exists(game.ModsDir)) return;
        foreach (var modDir in Directory.EnumerateDirectories(game.ModsDir))
        {
            ct.ThrowIfCancellationRequested();
            var infoJson = Path.Combine(modDir, "info.json");
            string? version = null;
            string name = Path.GetFileName(modDir);
            if (File.Exists(infoJson))
            {
                try
                {
                    var info = JsonSerializer.Deserialize<RedModInfo>(File.ReadAllText(infoJson), s_jsonOpts);
                    if (info != null)
                    {
                        if (!string.IsNullOrWhiteSpace(info.Name)) name = info.Name;
                        version = info.Version;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Failed to parse info.json at {Path}", infoJson);
                }
            }
            sink.Add(new DiscoveredMod(
                ModFramework.RedMod, name, version,
                modDir, Path.GetRelativePath(game.InstallDir, modDir)));
        }
    }

    private static void ScanLegacyArchive(GameInstallation game, List<DiscoveredMod> sink, CancellationToken ct)
    {
        if (!Directory.Exists(game.ArchiveModDir)) return;
        foreach (var f in Directory.EnumerateFiles(game.ArchiveModDir, "*.archive"))
        {
            ct.ThrowIfCancellationRequested();
            sink.Add(new DiscoveredMod(
                ModFramework.LegacyArchive,
                Path.GetFileNameWithoutExtension(f), null, f,
                Path.GetRelativePath(game.InstallDir, f)));
        }
        foreach (var f in Directory.EnumerateFiles(game.ArchiveModDir, "*.xl"))
        {
            sink.Add(new DiscoveredMod(
                ModFramework.ArchiveXL,
                Path.GetFileNameWithoutExtension(f), null, f,
                Path.GetRelativePath(game.InstallDir, f)));
        }
    }

    private static void ScanRed4ext(GameInstallation game, List<DiscoveredMod> sink, CancellationToken ct)
    {
        if (!Directory.Exists(game.Red4extPluginsDir)) return;
        foreach (var dir in Directory.EnumerateDirectories(game.Red4extPluginsDir))
        {
            ct.ThrowIfCancellationRequested();
            var hasDll = Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly).Any();
            if (!hasDll) continue;
            sink.Add(new DiscoveredMod(
                ModFramework.Red4ext,
                Path.GetFileName(dir), null, dir,
                Path.GetRelativePath(game.InstallDir, dir)));
        }
        foreach (var f in Directory.EnumerateFiles(game.Red4extPluginsDir, "*.dll", SearchOption.TopDirectoryOnly))
        {
            sink.Add(new DiscoveredMod(
                ModFramework.Red4ext,
                Path.GetFileNameWithoutExtension(f), null, f,
                Path.GetRelativePath(game.InstallDir, f)));
        }
    }

    private static void ScanCet(GameInstallation game, List<DiscoveredMod> sink, CancellationToken ct)
    {
        if (!Directory.Exists(game.CetModsDir)) return;
        foreach (var dir in Directory.EnumerateDirectories(game.CetModsDir))
        {
            ct.ThrowIfCancellationRequested();
            sink.Add(new DiscoveredMod(
                ModFramework.Cet,
                Path.GetFileName(dir), null, dir,
                Path.GetRelativePath(game.InstallDir, dir)));
        }
    }

    private static void ScanR6Tweaks(GameInstallation game, List<DiscoveredMod> sink, CancellationToken ct)
    {
        if (!Directory.Exists(game.R6TweaksDir)) return;
        foreach (var f in Directory.EnumerateFiles(game.R6TweaksDir, "*.yaml", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            sink.Add(new DiscoveredMod(
                ModFramework.TweakXL,
                Path.GetFileNameWithoutExtension(f), null, f,
                Path.GetRelativePath(game.InstallDir, f)));
        }
    }

    private static void ScanR6Scripts(GameInstallation game, List<DiscoveredMod> sink, CancellationToken ct)
    {
        if (!Directory.Exists(game.R6ScriptsDir)) return;
        foreach (var dir in Directory.EnumerateDirectories(game.R6ScriptsDir))
        {
            ct.ThrowIfCancellationRequested();
            var hasReds = Directory.EnumerateFiles(dir, "*.reds", SearchOption.AllDirectories).Any();
            if (!hasReds) continue;
            sink.Add(new DiscoveredMod(
                ModFramework.Redscript,
                Path.GetFileName(dir), null, dir,
                Path.GetRelativePath(game.InstallDir, dir)));
        }
    }
}
