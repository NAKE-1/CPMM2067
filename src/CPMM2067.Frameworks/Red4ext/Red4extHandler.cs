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

namespace CPMM2067.Frameworks.Red4ext;

public sealed class Red4extHandler : IModFrameworkHandler
{
    public ModFramework Framework => ModFramework.Red4ext;
    public bool SupportsLoadOrder => false;

    private readonly ILogger<Red4extHandler> _log;

    public Red4extHandler(ILogger<Red4extHandler> log) => _log = log;

    public Task<ModFramework> DetectAsync(string extractedRootDir, CancellationToken ct = default)
    {
        if (!Directory.Exists(extractedRootDir)) return Task.FromResult(ModFramework.Unknown);

        // Patterns:
        // 1. extractedRoot/red4ext/plugins/<name>/<files>
        // 2. extractedRoot/bin/x64/d3d11.dll  (RED4ext loader itself — installs it)
        // 3. extractedRoot/red4ext/<files>    (loader payload)
        if (Directory.Exists(Path.Combine(extractedRootDir, "red4ext"))) return Task.FromResult(ModFramework.Red4ext);
        if (Directory.Exists(Path.Combine(extractedRootDir, "bin", "x64"))
            && Directory.EnumerateFiles(Path.Combine(extractedRootDir, "bin", "x64"), "*.dll", SearchOption.TopDirectoryOnly).Any())
            return Task.FromResult(ModFramework.Red4ext);
        return Task.FromResult(ModFramework.Unknown);
    }

    public async Task<ModInstallationState> InstallAsync(
        ModInstallationRequest request,
        GameInstallation game,
        CancellationToken ct = default)
    {
        var root = request.ExtractedRootDir;
        var modId = ModId.NewId();
        var files = new List<InstalledFileRecord>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subroot in new[] { "red4ext", "bin", "r6" })
        {
            var srcSub = Path.Combine(root, subroot);
            if (!Directory.Exists(srcSub)) continue;
            foreach (var src in Directory.EnumerateFiles(srcSub, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(root, src);
                var dst = Path.Combine(game.InstallDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

                var overwrote = File.Exists(dst);
                File.Copy(src, dst, overwrite: true);
                if (!seen.Add(rel)) continue;

                var sha = await FileBackupStore.HashAsync(dst, ct).ConfigureAwait(false);
                files.Add(new InstalledFileRecord
                {
                    OwnerMod = modId,
                    RelativePath = rel,
                    Sha256 = sha,
                    SizeBytes = new FileInfo(dst).Length,
                    OverwroteVanilla = overwrote,
                });
            }
        }

        if (files.Count == 0)
            throw new InvalidOperationException("No RED4ext-compatible files found in archive");

        var manifest = new ModManifest
        {
            Id = modId,
            Name = request.SuggestedName,
            Version = request.Version,
            Framework = ModFramework.Red4ext,
            Source = request.Source,
            OriginalArchivePath = request.OriginalArchivePath,
            OriginalArchiveSha256 = request.OriginalArchiveSha256,
        };
        _log.LogInformation("Installed RED4ext payload {Name} — {Count} file(s)", manifest.Name, files.Count);

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
