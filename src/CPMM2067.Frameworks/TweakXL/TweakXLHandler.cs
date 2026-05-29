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

namespace CPMM2067.Frameworks.TweakXL;

/// <summary>
/// Installs TweakXL mods: arbitrary YAML files under r6/tweaks/. Triggered when an
/// extracted archive has an r6/tweaks/*.yaml payload AND nothing higher-priority
/// (REDmod info.json, RED4ext loader, legacy .archive) was found first.
/// </summary>
public sealed class TweakXLHandler : IModFrameworkHandler
{
    public ModFramework Framework => ModFramework.TweakXL;
    public bool SupportsLoadOrder => false;

    private readonly ILogger<TweakXLHandler> _log;

    public TweakXLHandler(ILogger<TweakXLHandler> log) => _log = log;

    public Task<ModFramework> DetectAsync(string extractedRootDir, CancellationToken ct = default)
    {
        if (!Directory.Exists(extractedRootDir)) return Task.FromResult(ModFramework.Unknown);
        var tweaksDir = Path.Combine(extractedRootDir, "r6", "tweaks");
        if (Directory.Exists(tweaksDir) &&
            Directory.EnumerateFiles(tweaksDir, "*.yaml", SearchOption.AllDirectories).Any())
        {
            return Task.FromResult(ModFramework.TweakXL);
        }
        return Task.FromResult(ModFramework.Unknown);
    }

    public async Task<ModInstallationState> InstallAsync(
        ModInstallationRequest request,
        GameInstallation game,
        CancellationToken ct = default)
    {
        var srcTweaks = Path.Combine(request.ExtractedRootDir, "r6", "tweaks");
        if (!Directory.Exists(srcTweaks))
            throw new InvalidOperationException("No r6/tweaks/ payload found in archive");

        Directory.CreateDirectory(game.R6TweaksDir);
        var modId = ModId.NewId();
        var files = new List<InstalledFileRecord>();

        foreach (var src in Directory.EnumerateFiles(srcTweaks, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relUnderTweaks = Path.GetRelativePath(srcTweaks, src);
            var dst = Path.Combine(game.R6TweaksDir, relUnderTweaks);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);

            var gameRel = Path.GetRelativePath(game.InstallDir, dst);
            var sha = await FileBackupStore.HashAsync(dst, ct).ConfigureAwait(false);
            files.Add(new InstalledFileRecord
            {
                OwnerMod = modId,
                RelativePath = gameRel,
                Sha256 = sha,
                SizeBytes = new FileInfo(dst).Length,
                OverwroteVanilla = false,
            });
        }

        if (files.Count == 0)
            throw new InvalidOperationException("Empty TweakXL payload");

        var manifest = new ModManifest
        {
            Id = modId,
            Name = request.SuggestedName,
            Version = request.Version,
            Framework = ModFramework.TweakXL,
            Source = request.Source,
            OriginalArchivePath = request.OriginalArchivePath,
            OriginalArchiveSha256 = request.OriginalArchiveSha256,
        };
        _log.LogInformation("Installed TweakXL payload {Name} — {Count} yaml file(s)", manifest.Name, files.Count);

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
            if (File.Exists(abs)) File.Delete(abs);
        }
        // Prune empty subdirs under r6/tweaks/
        foreach (var f in state.Files)
        {
            var dir = Path.GetDirectoryName(Path.Combine(game.InstallDir, f.RelativePath));
            while (!string.IsNullOrEmpty(dir)
                   && dir.Length > game.R6TweaksDir.Length
                   && Directory.Exists(dir)
                   && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                dir = Path.GetDirectoryName(dir);
            }
        }
        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(ModInstallationState state, ModEnabled target, GameInstallation game, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<ModInstallationState>> ReorderAsync(
        IReadOnlyList<ModInstallationState> ordered,
        GameInstallation game,
        CancellationToken ct = default)
        => Task.FromResult(ordered);

    public Task DeployAsync(GameInstallation game, CancellationToken ct = default) => Task.CompletedTask;
}
