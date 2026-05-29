using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CPMM2067.Archives.Fomod;
using CPMM2067.Backup;
using CPMM2067.Core.Game;
using CPMM2067.Core.Mods;
using Microsoft.Extensions.Logging;

namespace CPMM2067.Frameworks.Fomod;

/// <summary>
/// FOMOD installer that detects fomod/ModuleConfig.xml and applies the resolved file list with
/// auto-selected defaults. Multi-step wizard UI is queued for a later release.
/// </summary>
public sealed class FomodHandler
{
    private readonly ILogger<FomodHandler> _log;

    public FomodHandler(ILogger<FomodHandler> log) => _log = log;

    public bool IsFomod(string extractedRootDir, out string moduleConfigPath)
        => FomodParser.IsFomod(extractedRootDir, out moduleConfigPath);

    public FomodPlan Resolve(string moduleConfigPath, string extractedRootDir)
        => FomodParser.Resolve(moduleConfigPath, extractedRootDir);

    /// <summary>
    /// Apply a resolved FOMOD plan into the game install. Returns the installed file list so
    /// callers can build a journal entry.
    /// </summary>
    public async Task<ModInstallationState> ApplyAsync(
        FomodPlan plan,
        string extractedRootDir,
        string suggestedName,
        GameInstallation game,
        CancellationToken ct = default)
    {
        var modId = ModId.NewId();
        var files = new List<InstalledFileRecord>();

        foreach (var f in plan.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(f.SourceAbsolute) || !File.Exists(f.SourceAbsolute) && !Directory.Exists(f.SourceAbsolute))
                continue;

            if (f.IsFolder && Directory.Exists(f.SourceAbsolute))
            {
                foreach (var src in Directory.EnumerateFiles(f.SourceAbsolute, "*", SearchOption.AllDirectories))
                {
                    var relUnder = Path.GetRelativePath(f.SourceAbsolute, src);
                    var targetUnderGame = Path.Combine(f.Destination.Replace('/', Path.DirectorySeparatorChar), relUnder);
                    files.Add(await CopyFileAsync(src, targetUnderGame, game, modId, ct));
                }
            }
            else if (File.Exists(f.SourceAbsolute))
            {
                var targetUnderGame = string.IsNullOrEmpty(f.Destination)
                    ? Path.GetFileName(f.SourceAbsolute)
                    : f.Destination.Replace('/', Path.DirectorySeparatorChar);
                files.Add(await CopyFileAsync(f.SourceAbsolute, targetUnderGame, game, modId, ct));
            }
        }

        var manifest = new ModManifest
        {
            Id = modId,
            Name = string.IsNullOrEmpty(plan.ModuleName) ? suggestedName : plan.ModuleName,
            Version = "1.0",
            Framework = ModFramework.Unknown,
            Source = ModSource.LocalFile,
        };

        _log.LogInformation("Applied FOMOD '{Name}' — {Count} file(s)", manifest.Name, files.Count);

        return new ModInstallationState
        {
            Manifest = manifest,
            State = ModEnabled.Enabled,
            Files = files,
            LoadOrder = 0,
        };
    }

    private static async Task<InstalledFileRecord> CopyFileAsync(
        string src, string relUnderGame, GameInstallation game, ModId modId, CancellationToken ct)
    {
        var dst = Path.Combine(game.InstallDir, relUnderGame);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        var overwrote = File.Exists(dst);
        File.Copy(src, dst, overwrite: true);
        var sha = await FileBackupStore.HashAsync(dst, ct).ConfigureAwait(false);
        return new InstalledFileRecord
        {
            OwnerMod = modId,
            RelativePath = Path.GetRelativePath(game.InstallDir, dst),
            Sha256 = sha,
            SizeBytes = new FileInfo(dst).Length,
            OverwroteVanilla = overwrote,
        };
    }

}
