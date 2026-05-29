using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CPMM2067.Backup;
using CPMM2067.Core.Game;
using CPMM2067.Core.Mods;
using Microsoft.Extensions.Logging;

namespace CPMM2067.Frameworks.LegacyArchive;

public sealed class LegacyArchiveHandler : IModFrameworkHandler
{
    public ModFramework Framework => ModFramework.LegacyArchive;
    public bool SupportsLoadOrder => true;

    private readonly ILogger<LegacyArchiveHandler> _log;

    public LegacyArchiveHandler(ILogger<LegacyArchiveHandler> log) => _log = log;

    public Task<ModFramework> DetectAsync(string extractedRootDir, CancellationToken ct = default)
    {
        if (FindArchiveFiles(extractedRootDir).Any())
            return Task.FromResult(ModFramework.LegacyArchive);
        return Task.FromResult(ModFramework.Unknown);
    }

    public async Task<ModInstallationState> InstallAsync(
        ModInstallationRequest request,
        GameInstallation game,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(game.ArchiveModDir);
        var sources = FindArchiveFiles(request.ExtractedRootDir).ToList();
        if (sources.Count == 0)
            throw new InvalidOperationException("No .archive files found in extracted archive");

        var modId = ModId.NewId();
        var files = new List<InstalledFileRecord>();

        foreach (var src in sources)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(src);
            var dst = Path.Combine(game.ArchiveModDir, name);
            File.Copy(src, dst, overwrite: false);
            var rel = Path.GetRelativePath(game.InstallDir, dst);
            var sha = await FileBackupStore.HashAsync(dst, ct).ConfigureAwait(false);
            files.Add(new InstalledFileRecord
            {
                OwnerMod = modId,
                RelativePath = rel,
                Sha256 = sha,
                SizeBytes = new FileInfo(dst).Length,
                OverwroteVanilla = false,
            });
        }

        var manifest = new ModManifest
        {
            Id = modId,
            Name = request.SuggestedName,
            Version = request.Version,
            Framework = ModFramework.LegacyArchive,
            Source = request.Source,
            OriginalArchivePath = request.OriginalArchivePath,
            OriginalArchiveSha256 = request.OriginalArchiveSha256,
        };
        _log.LogInformation("Installed {Count} legacy .archive file(s) for {Name}", files.Count, manifest.Name);
        return new ModInstallationState
        {
            Manifest = manifest,
            State = ModEnabled.Enabled,
            Files = files,
            LoadOrder = 0,
        };
    }

    public Task UninstallAsync(ModInstallationState state, GameInstallation game, CancellationToken ct = default)
    {
        foreach (var f in state.Files)
        {
            var abs = Path.Combine(game.InstallDir, f.RelativePath);
            if (File.Exists(abs)) { File.Delete(abs); _log.LogInformation("Removed {Path}", abs); }
        }
        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(ModInstallationState state, ModEnabled target, GameInstallation game, CancellationToken ct = default)
        => Task.CompletedTask; // legacy archives have no on/off — only present or absent

    public Task<IReadOnlyList<ModInstallationState>> ReorderAsync(
        IReadOnlyList<ModInstallationState> ordered,
        GameInstallation game,
        CancellationToken ct = default)
    {
        // Legacy .archive load order is ASCII filename order — rename with NN_ prefixes.
        for (var i = 0; i < ordered.Count; i++)
        {
            foreach (var f in ordered[i].Files)
            {
                var abs = Path.Combine(game.InstallDir, f.RelativePath);
                if (!File.Exists(abs)) continue;
                var dir = Path.GetDirectoryName(abs)!;
                var name = Path.GetFileName(abs);
                var stripped = System.Text.RegularExpressions.Regex.Replace(name, @"^\d{2,3}_", "");
                var newName = $"{i:00}_{stripped}";
                var newAbs = Path.Combine(dir, newName);
                if (newAbs != abs) File.Move(abs, newAbs, overwrite: true);
            }
        }
        return Task.FromResult<IReadOnlyList<ModInstallationState>>(ordered);
    }

    public Task DeployAsync(GameInstallation game, CancellationToken ct = default) => Task.CompletedTask;

    private static IEnumerable<string> FindArchiveFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var f in Directory.EnumerateFiles(root, "*.archive", SearchOption.AllDirectories))
            yield return f;
        foreach (var f in Directory.EnumerateFiles(root, "*.xl", SearchOption.AllDirectories))
            yield return f;
    }
}
