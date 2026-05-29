using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CPMM2067.Core.Game;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace CPMM2067.GameDetect;

public sealed class GameDetector
{
    public const string SteamAppId = "1091500";
    public const string GogProductId = "1423049311";
    private readonly ILogger<GameDetector> _log;

    public GameDetector(ILogger<GameDetector> log) => _log = log;

    public async Task<GameInstallation?> DetectAsync(CancellationToken ct = default)
    {
        foreach (var probe in new Func<CancellationToken, Task<GameInstallation?>>[]
        {
            DetectSteamAsync,
            DetectGogAsync,
            DetectEpicAsync,
        })
        {
            ct.ThrowIfCancellationRequested();
            var hit = await probe(ct).ConfigureAwait(false);
            if (hit != null)
            {
                _log.LogInformation("Detected CP2077 via {Storefront} at {Path} (v{Version})",
                    hit.Storefront, hit.InstallDir, hit.Version);
                return hit;
            }
        }
        _log.LogWarning("CP2077 not detected via any storefront");
        return null;
    }

    public GameInstallation? FromManualPath(string path)
    {
        if (!IsValidInstall(path)) return null;
        return Build(path, GameStorefront.Manual, null);
    }

    public bool IsValidInstall(string path)
        => !string.IsNullOrWhiteSpace(path)
        && File.Exists(Path.Combine(path, "bin", "x64", "Cyberpunk2077.exe"));

    private Task<GameInstallation?> DetectSteamAsync(CancellationToken ct)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return Task.FromResult<GameInstallation?>(null);
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                          ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            if (key?.GetValue("InstallPath") is not string steamPath) return Task.FromResult<GameInstallation?>(null);

            var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            foreach (var libraryRoot in EnumerateSteamLibraryRoots(libraryFile, steamPath))
            {
                var manifest = Path.Combine(libraryRoot, "steamapps", $"appmanifest_{SteamAppId}.acf");
                if (!File.Exists(manifest)) continue;
                var installDir = Path.Combine(libraryRoot, "steamapps", "common", "Cyberpunk 2077");
                if (IsValidInstall(installDir))
                {
                    return Task.FromResult<GameInstallation?>(Build(installDir, GameStorefront.Steam, SteamAppId));
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Steam detection failed");
        }
        return Task.FromResult<GameInstallation?>(null);
    }

    private static IEnumerable<string> EnumerateSteamLibraryRoots(string libraryFile, string steamRoot)
    {
        yield return steamRoot;
        if (!File.Exists(libraryFile)) yield break;
        foreach (var line in File.ReadLines(libraryFile))
        {
            var idx = line.IndexOf("\"path\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            var first = line.IndexOf('"', idx + 6);
            if (first < 0) continue;
            var second = line.IndexOf('"', first + 1);
            if (second < 0) continue;
            var third = line.IndexOf('"', second + 1);
            if (third < 0) continue;
            var fourth = line.IndexOf('"', third + 1);
            if (fourth < 0) continue;
            var path = line.Substring(third + 1, fourth - third - 1).Replace("\\\\", "\\");
            if (Directory.Exists(path)) yield return path;
        }
    }

    private Task<GameInstallation?> DetectGogAsync(CancellationToken ct)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return Task.FromResult<GameInstallation?>(null);
            using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\WOW6432Node\GOG.com\Games\{GogProductId}")
                          ?? Registry.LocalMachine.OpenSubKey($@"SOFTWARE\GOG.com\Games\{GogProductId}");
            if (key?.GetValue("path") is not string installDir) return Task.FromResult<GameInstallation?>(null);
            if (!IsValidInstall(installDir)) return Task.FromResult<GameInstallation?>(null);
            var buildId = key.GetValue("buildId") as string;
            return Task.FromResult<GameInstallation?>(Build(installDir, GameStorefront.Gog, buildId ?? GogProductId));
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "GOG detection failed");
            return Task.FromResult<GameInstallation?>(null);
        }
    }

    private Task<GameInstallation?> DetectEpicAsync(CancellationToken ct)
    {
        try
        {
            var manifestsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(manifestsDir)) return Task.FromResult<GameInstallation?>(null);

            foreach (var file in Directory.EnumerateFiles(manifestsDir, "*.item"))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    var root = doc.RootElement;
                    var displayName = root.TryGetProperty("DisplayName", out var n) ? n.GetString() : null;
                    var installLoc = root.TryGetProperty("InstallLocation", out var i) ? i.GetString() : null;
                    if (string.IsNullOrWhiteSpace(installLoc)) continue;
                    if (displayName?.Contains("Cyberpunk", StringComparison.OrdinalIgnoreCase) != true) continue;
                    if (!IsValidInstall(installLoc)) continue;
                    var appName = root.TryGetProperty("AppName", out var a) ? a.GetString() : null;
                    return Task.FromResult<GameInstallation?>(Build(installLoc, GameStorefront.Epic, appName));
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Epic manifest parse failed: {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Epic detection failed");
        }
        return Task.FromResult<GameInstallation?>(null);
    }

    private GameInstallation Build(string installDir, GameStorefront sf, string? appId)
    {
        var exe = Path.Combine(installDir, "bin", "x64", "Cyberpunk2077.exe");
        var version = GameVersion.Unknown;
        try
        {
            var fvi = FileVersionInfo.GetVersionInfo(exe);
            if (GameVersion.TryParse(fvi.ProductVersion ?? fvi.FileVersion, out var v)) version = v;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Version read failed for {Exe}", exe);
        }

        var redModExe = Path.Combine(installDir, "tools", "redmod", "bin", "redMod.exe");
        return new GameInstallation
        {
            InstallDir = installDir,
            Storefront = sf,
            StorefrontAppId = appId,
            Version = version,
            RedModInstalled = File.Exists(redModExe),
        };
    }
}
