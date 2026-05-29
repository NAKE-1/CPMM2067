using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CPMM2067.App.Services;

public sealed record CyberCatInstallResult(bool Ok, string Message, string? ExePath);

/// <summary>
/// Downloads the latest CyberCAT-SimpleGUI release from Deweh/CyberCAT-SimpleGUI on GitHub,
/// extracts it under &lt;exe-dir&gt;\tools\CyberCAT-SimpleGUI\, and returns the path to
/// CP2077SaveEditor.exe (or whichever .exe was inside the zip).
/// </summary>
public static class CyberCatInstaller
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/Deweh/CyberCAT-SimpleGUI/releases/latest";

    public static string InstallRoot =>
        Path.Combine(AppContext.BaseDirectory, "tools", "CyberCAT-SimpleGUI");

    public static async Task<CyberCatInstallResult> DownloadAndInstallAsync(
        Action<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Invoke("Querying GitHub for the latest CyberCAT-SimpleGUI release…");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CPMM2067/0.2 (+https://github.com/NAKE-1/CPMM2067)");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        ReleaseInfo? release;
        try
        {
            release = await http.GetFromJsonAsync<ReleaseInfo>(LatestReleaseUrl, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new(false, $"GitHub API call failed: {ex.Message}", null);
        }
        if (release?.Assets == null || release.Assets.Length == 0)
            return new(false, "No assets in the latest CyberCAT-SimpleGUI release.", null);

        // Pick the first .zip asset; fall back to the first asset overall.
        var asset = release.Assets.FirstOrDefault(a =>
                        a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
                    ?? release.Assets[0];
        if (string.IsNullOrEmpty(asset.BrowserDownloadUrl))
            return new(false, "Latest release asset has no download URL.", null);

        Directory.CreateDirectory(InstallRoot);
        var zipPath = Path.Combine(InstallRoot, asset.Name ?? "release.zip");

        progress?.Invoke($"Downloading {asset.Name} ({release.TagName})…");
        try
        {
            using var resp = await http.GetAsync(asset.BrowserDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(zipPath);
            await src.CopyToAsync(dst, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new(false, $"Download failed: {ex.Message}", null);
        }

        progress?.Invoke("Extracting…");
        var extractDir = Path.Combine(InstallRoot, "extracted");
        try
        {
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
        }
        catch (Exception ex)
        {
            return new(false, $"Extraction failed: {ex.Message}", null);
        }

        // Locate the editor exe — name is usually CP2077SaveEditor.exe, but accept any .exe
        var exe = Directory.EnumerateFiles(extractDir, "CP2077SaveEditor.exe", SearchOption.AllDirectories)
                      .FirstOrDefault()
                  ?? Directory.EnumerateFiles(extractDir, "*.exe", SearchOption.AllDirectories)
                      .FirstOrDefault(p => !p.Contains("uninst", StringComparison.OrdinalIgnoreCase));
        if (exe == null)
            return new(false, "No .exe found inside the extracted zip.", null);

        try { File.Delete(zipPath); } catch { /* leave it; harmless */ }

        return new(true, $"Installed CyberCAT-SimpleGUI {release.TagName} -> {exe}", exe);
    }

    private sealed class ReleaseInfo
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("assets")] public AssetInfo[]? Assets { get; set; }
    }

    private sealed class AssetInfo
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}
