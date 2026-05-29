using System;
using System.IO;

namespace CPMM2067.Core;

/// <summary>
/// Portable-first paths. By default data lives in &lt;exe-dir&gt;\data\, keeping the app
/// self-contained next to its executable. An installer can override the location by
/// dropping a one-line file named "datadir.cfg" alongside the exe whose contents is the
/// absolute path of the preferred data root (e.g. %AppData%\CPMM2067).
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "CPMM2067";
    public const string OverrideMarker = "datadir.cfg";

    private static readonly Lazy<string> s_root = new(ResolveRoot);
    public static string AppData => s_root.Value;

    public static string LogsDir => Path.Combine(AppData, "logs");
    public static string BackupsDir => Path.Combine(AppData, "backups");
    public static string CacheDir => Path.Combine(AppData, "cache");
    public static string ArchiveCacheDir => Path.Combine(CacheDir, "archives");
    public static string ProfilesDir => Path.Combine(AppData, "profiles");
    public static string SavesBackupDir => Path.Combine(BackupsDir, "saves");
    public static string ConfigSnapshotDir => Path.Combine(BackupsDir, "r6config");
    public static string ManifestDb => Path.Combine(AppData, "manifest.db");
    public static string SettingsFile => Path.Combine(AppData, "settings.json");

    /// <summary>
    /// CP2077 save location. Modern installs (2.x) use %USERPROFILE%\Saved Games\CD Projekt Red\Cyberpunk 2077.
    /// Older builds and some GOG installs use Documents\CD Projekt Red\Cyberpunk 2077.
    /// We prefer whichever exists; if neither, default to Saved Games.
    /// </summary>
    public static string SavesFolder
    {
        get
        {
            var savedGames = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Saved Games", "CD Projekt Red", "Cyberpunk 2077");
            if (Directory.Exists(savedGames)) return savedGames;
            var documents = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "CD Projekt Red", "Cyberpunk 2077");
            if (Directory.Exists(documents)) return documents;
            return savedGames; // default suggestion even when missing
        }
    }

    private static string ResolveRoot()
    {
        // 1. Installer override: <exe-dir>\datadir.cfg
        var exeDir = AppContext.BaseDirectory;
        var marker = Path.Combine(exeDir, OverrideMarker);
        if (File.Exists(marker))
        {
            try
            {
                var line = File.ReadAllText(marker).Trim();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var expanded = Environment.ExpandEnvironmentVariables(line);
                    return expanded;
                }
            }
            catch { /* fall through */ }
        }

        // 2. Portable default: <exe-dir>\data
        return Path.Combine(exeDir, "data");
    }

    public static void EnsureAll()
    {
        Directory.CreateDirectory(AppData);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(BackupsDir);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(ArchiveCacheDir);
        Directory.CreateDirectory(ProfilesDir);
        Directory.CreateDirectory(SavesBackupDir);
        Directory.CreateDirectory(ConfigSnapshotDir);
    }
}
