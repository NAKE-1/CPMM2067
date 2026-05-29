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

namespace CPMM2067.Frameworks.Cet;

/// <summary>
/// Installs Cyber Engine Tweaks (CET) Lua mods. Detected when the archive contains a
/// bin/x64/plugins/cyber_engine_tweaks/mods/&lt;modname&gt;/ subtree.
/// </summary>
public sealed class CetHandler : IModFrameworkHandler
{
    public ModFramework Framework => ModFramework.Cet;
    public bool SupportsLoadOrder => false;

    private const string CetSubPath = @"bin\x64\plugins\cyber_engine_tweaks\mods";

    private readonly ILogger<CetHandler> _log;

    public CetHandler(ILogger<CetHandler> log) => _log = log;

    public Task<ModFramework> DetectAsync(string extractedRootDir, CancellationToken ct = default)
    {
        if (!Directory.Exists(extractedRootDir)) return Task.FromResult(ModFramework.Unknown);
        var cetRoot = Path.Combine(extractedRootDir, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods");
        if (Directory.Exists(cetRoot)
            && Directory.EnumerateDirectories(cetRoot).Any())
        {
            return Task.FromResult(ModFramework.Cet);
        }
        return Task.FromResult(ModFramework.Unknown);
    }

    public async Task<ModInstallationState> InstallAsync(
        ModInstallationRequest request,
        GameInstallation game,
        CancellationToken ct = default)
    {
        var srcCet = Path.Combine(request.ExtractedRootDir, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods");
        if (!Directory.Exists(srcCet))
            throw new InvalidOperationException("No CET payload found in archive");

        Directory.CreateDirectory(game.CetModsDir);
        var modId = ModId.NewId();
        var files = new List<InstalledFileRecord>();

        foreach (var src in Directory.EnumerateFiles(srcCet, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relUnderCet = Path.GetRelativePath(srcCet, src);
            var dst = Path.Combine(game.CetModsDir, relUnderCet);
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
            throw new InvalidOperationException("Empty CET payload");

        var manifest = new ModManifest
        {
            Id = modId,
            Name = request.SuggestedName,
            Version = request.Version,
            Framework = ModFramework.Cet,
            Source = request.Source,
            OriginalArchivePath = request.OriginalArchivePath,
            OriginalArchiveSha256 = request.OriginalArchiveSha256,
        };
        _log.LogInformation("Installed CET payload {Name} — {Count} file(s)", manifest.Name, files.Count);

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
        // Prune empty subdirs under the CET mods root
        foreach (var f in state.Files)
        {
            var dir = Path.GetDirectoryName(Path.Combine(game.InstallDir, f.RelativePath));
            while (!string.IsNullOrEmpty(dir)
                   && dir.Length > game.CetModsDir.Length
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
