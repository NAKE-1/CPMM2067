using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace CPMM2067.Archives;

public sealed class ArchiveExtractor
{
    private readonly ILogger<ArchiveExtractor> _log;

    public ArchiveExtractor(ILogger<ArchiveExtractor> log) => _log = log;

    public async Task<string> ExtractToTempAsync(string archivePath, CancellationToken ct = default)
        => await ExtractToAsync(archivePath, Path.Combine(Path.GetTempPath(), "cpmm2067", Guid.NewGuid().ToString("N")), ct);

    public async Task<string> ExtractToAsync(string archivePath, string targetDir, CancellationToken ct = default)
    {
        if (!File.Exists(archivePath)) throw new FileNotFoundException(archivePath);
        Directory.CreateDirectory(targetDir);
        await Task.Run(() => ExtractCore(archivePath, targetDir, ct), ct).ConfigureAwait(false);
        _log.LogInformation("Extracted {Archive} -> {Dir}", archivePath, targetDir);
        return targetDir;
    }

    private static void ExtractCore(string archivePath, string targetDir, CancellationToken ct)
    {
        var ext = Path.GetExtension(archivePath).ToLowerInvariant();
        switch (ext)
        {
            case ".zip":
                ZipFile.ExtractToDirectory(archivePath, targetDir, overwriteFiles: true);
                return;
            case ".7z":
                using (var sz = SevenZipArchive.OpenArchive(archivePath, new ReaderOptions()))
                    ExtractArchive(sz, targetDir, ct);
                return;
            case ".rar":
                using (var rar = RarArchive.OpenArchive(archivePath, new ReaderOptions()))
                    ExtractArchive(rar, targetDir, ct);
                return;
            default:
                throw new NotSupportedException($"Unsupported archive type: {ext}");
        }
    }

    private static void ExtractArchive(IArchive archive, string targetDir, CancellationToken ct)
    {
        var opts = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.IsDirectory) continue;
            entry.WriteToDirectory(targetDir, opts);
        }
    }
}
