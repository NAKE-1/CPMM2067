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

namespace CPMM2067.Frameworks.Redscript;

/// <summary>
/// Installs standalone redscript / Codeware-driven mods: *.reds files under r6/scripts/.
/// Triggered only when nothing higher-priority matched (REDmod, CET, RED4ext, TweakXL).
/// Combined RED4ext + redscript payloads are caught by Red4extHandler which copies the
/// red4ext\, bin\, AND r6\ subtrees together.
/// </summary>
public sealed class RedscriptHandler : IModFrameworkHandler
{
    public ModFramework Framework => ModFramework.Redscript;
    public bool SupportsLoadOrder => false;

    private readonly ILogger<RedscriptHandler> _log;

    public RedscriptHandler(ILogger<RedscriptHandler> log) => _log = log;

    public Task<ModFramework> DetectAsync(string extractedRootDir, CancellationToken ct = default)
    {
        if (!Directory.Exists(extractedRootDir)) return Task.FromResult(ModFramework.Unknown);
        var scriptsDir = Path.Combine(extractedRootDir, "r6", "scripts");
        if (Directory.Exists(scriptsDir)
            && Directory.EnumerateFiles(scriptsDir, "*.reds", SearchOption.AllDirectories).Any())
        {
            return Task.FromResult(ModFramework.Redscript);
        }
        return Task.FromResult(ModFramework.Unknown);
    }

    public async Task<ModInstallationState> InstallAsync(
        ModInstallationRequest request,
        GameInstallation game,
        CancellationToken ct = default)
    {
        var srcScripts = Path.Combine(request.ExtractedRootDir, "r6", "scripts");
        if (!Directory.Exists(srcScripts))
            throw new InvalidOperationException("No r6/scripts/ payload found in archive");

        Directory.CreateDirectory(game.R6ScriptsDir);
        var modId = ModId.NewId();
        var files = new List<InstalledFileRecord>();

        foreach (var src in Directory.EnumerateFiles(srcScripts, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relUnderScripts = Path.GetRelativePath(srcScripts, src);
            var dst = Path.Combine(game.R6ScriptsDir, relUnderScripts);
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
            throw new InvalidOperationException("Empty redscript payload");

        var manifest = new ModManifest
        {
            Id = modId,
            Name = request.SuggestedName,
            Version = request.Version,
            Framework = ModFramework.Redscript,
            Source = request.Source,
            OriginalArchivePath = request.OriginalArchivePath,
            OriginalArchiveSha256 = request.OriginalArchiveSha256,
        };
        _log.LogInformation("Installed redscript payload {Name} — {Count} file(s)", manifest.Name, files.Count);

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
        foreach (var f in state.Files)
        {
            var dir = Path.GetDirectoryName(Path.Combine(game.InstallDir, f.RelativePath));
            while (!string.IsNullOrEmpty(dir)
                   && dir.Length > game.R6ScriptsDir.Length
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
