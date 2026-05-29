using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using CPMM2067.Core;
using CPMM2067.Core.Game;

namespace CPMM2067.Diagnostics;

public static class DiagnosticBundle
{
    public static async Task<string> CreateAsync(
        string outputDir,
        GameInstallation? game,
        string? appVersion = null)
    {
        Directory.CreateDirectory(outputDir);
        var path = Path.Combine(outputDir, $"cpmm2067-diag-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        if (Directory.Exists(AppPaths.LogsDir))
        {
            foreach (var f in Directory.EnumerateFiles(AppPaths.LogsDir))
            {
                zip.CreateEntryFromFile(f, $"logs/{Path.GetFileName(f)}");
            }
        }

        var meta = new
        {
            CreatedUtc = DateTime.UtcNow,
            AppVersion = appVersion ?? "dev",
            OsVersion = Environment.OSVersion.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            Is64BitOs = Environment.Is64BitOperatingSystem,
            Game = game is null ? null : new
            {
                game.InstallDir,
                Storefront = game.Storefront.ToString(),
                Version = game.Version.ToString(),
                game.StorefrontAppId,
                game.RedModInstalled,
            },
        };

        var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
        var entry = zip.CreateEntry("metadata.json");
        await using (var es = entry.Open())
        await using (var sw = new StreamWriter(es))
            await sw.WriteAsync(json);

        if (File.Exists(AppPaths.SettingsFile))
        {
            zip.CreateEntryFromFile(AppPaths.SettingsFile, "settings.json");
        }

        return path;
    }
}
