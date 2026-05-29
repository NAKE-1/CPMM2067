using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CPMM2067.Core;
using CPMM2067.Core.Backups;
using CPMM2067.Core.Game;
using CPMM2067.Core.Mods;
using Microsoft.Extensions.Logging;

namespace CPMM2067.Backup;

public sealed class FileBackupStore : IBackupStore
{
    private readonly ILogger<FileBackupStore> _log;

    public FileBackupStore(ILogger<FileBackupStore> log) => _log = log;

    public async Task<BackupRecord?> BackupIfVanillaAsync(
        GameInstallation game,
        string relativePath,
        ModId owningMod,
        CancellationToken ct = default)
    {
        var absPath = Path.Combine(game.InstallDir, relativePath);
        if (!File.Exists(absPath)) return null;

        var info = new FileInfo(absPath);
        var sha = await HashAsync(absPath, ct).ConfigureAwait(false);

        var versionTag = SanitizeForPath(game.Version.Raw);
        var backupRel = Path.Combine(versionTag, relativePath);
        var backupAbs = Path.Combine(AppPaths.BackupsDir, backupRel);
        Directory.CreateDirectory(Path.GetDirectoryName(backupAbs)!);

        if (File.Exists(backupAbs))
        {
            _log.LogDebug("Backup already exists for {RelPath} -> reuse {BackupAbs}", relativePath, backupAbs);
        }
        else
        {
            await using var src = File.OpenRead(absPath);
            await using var dst = File.Create(backupAbs);
            await src.CopyToAsync(dst, ct).ConfigureAwait(false);
            _log.LogInformation("Backed up vanilla file {RelPath} -> {BackupAbs}", relativePath, backupAbs);
        }

        return new BackupRecord
        {
            RelativePath = relativePath,
            BackupAbsolutePath = backupAbs,
            OriginalSha256 = sha,
            OriginalSizeBytes = info.Length,
            GameVersion = game.Version.Raw,
        };
    }

    public async Task RestoreAsync(
        GameInstallation game,
        BackupRecord record,
        CancellationToken ct = default)
    {
        if (!File.Exists(record.BackupAbsolutePath))
        {
            _log.LogWarning("Backup missing for {RelPath} (expected at {BackupAbs}); leaving as-is",
                record.RelativePath, record.BackupAbsolutePath);
            return;
        }
        var dst = Path.Combine(game.InstallDir, record.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        await using (var src = File.OpenRead(record.BackupAbsolutePath))
        await using (var dstStream = File.Create(dst))
            await src.CopyToAsync(dstStream, ct).ConfigureAwait(false);
        _log.LogInformation("Restored vanilla file {RelPath} from backup", record.RelativePath);
    }

    public static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var s = File.OpenRead(path);
        using var sha = SHA256.Create();
        var bytes = await sha.ComputeHashAsync(s, ct).ConfigureAwait(false);
        return Convert.ToHexString(bytes);
    }

    private static string SanitizeForPath(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        Span<char> buf = stackalloc char[s.Length];
        for (var i = 0; i < s.Length; i++)
            buf[i] = Array.IndexOf(bad, s[i]) >= 0 ? '_' : s[i];
        return new string(buf);
    }
}
