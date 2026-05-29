using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using CPMM2067.Core;
using Microsoft.Extensions.Logging;

namespace CPMM2067.Backup;

public sealed class SavesSnapshotter
{
    private readonly ILogger<SavesSnapshotter> _log;

    public SavesSnapshotter(ILogger<SavesSnapshotter> log) => _log = log;

    public async Task<string?> SnapshotAsync(string? reason = null, CancellationToken ct = default)
    {
        var savesDir = AppPaths.SavesFolder;
        if (!Directory.Exists(savesDir))
        {
            _log.LogWarning("Saves dir not found at {Path}", savesDir);
            return null;
        }

        Directory.CreateDirectory(AppPaths.SavesBackupDir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var tag = string.IsNullOrWhiteSpace(reason) ? "snap" : Sanitize(reason);
        var zipPath = Path.Combine(AppPaths.SavesBackupDir, $"saves-{stamp}-{tag}.zip");

        await Task.Run(() =>
        {
            ZipFile.CreateFromDirectory(savesDir, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
        }, ct).ConfigureAwait(false);

        _log.LogInformation("Snapshotted saves -> {Path}", zipPath);
        return zipPath;
    }

    private static string Sanitize(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        Span<char> buf = stackalloc char[s.Length];
        for (var i = 0; i < s.Length; i++)
            buf[i] = Array.IndexOf(bad, s[i]) >= 0 ? '_' : s[i];
        return new string(buf);
    }
}
