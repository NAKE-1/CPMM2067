using System;
using System.Diagnostics;
using System.IO;

namespace CPMM2067.App.Services;

/// <summary>
/// Opens URLs in the user's preferred browser exe (from settings) instead of going through
/// the Windows default handler — which is often Edge even when the user has Chrome/Firefox
/// installed and never picks them as default.
/// </summary>
public static class BrowserLauncher
{
    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var exe = AppHost.Settings.PreferredBrowserExe;
        if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = '"' + url + '"',
                    UseShellExecute = false,
                });
                return;
            }
            catch { /* fall through to system default */ }
        }
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* swallow — best effort */ }
    }

    private static readonly string[] s_candidatePaths = new[]
    {
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files\Mozilla Firefox\firefox.exe",
        @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe",
        @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe",
        @"C:\Program Files\Vivaldi\Application\vivaldi.exe",
        @"%LOCALAPPDATA%\Programs\Opera\opera.exe",
        @"%LOCALAPPDATA%\Programs\Opera GX\opera.exe",
        @"%LOCALAPPDATA%\Vivaldi\Application\vivaldi.exe",
        @"%LOCALAPPDATA%\Programs\Zen Browser\zen.exe",
        @"%PROGRAMFILES%\LibreWolf\librewolf.exe",
        // Edge intentionally last — only if nothing else found
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    };

    /// <summary>Returns the first browser exe found on disk, excluding Edge unless it's the only option.</summary>
    public static string? AutoDetect()
    {
        string? edge = null;
        foreach (var raw in s_candidatePaths)
        {
            var p = Environment.ExpandEnvironmentVariables(raw);
            if (!File.Exists(p)) continue;
            if (p.Contains("msedge", StringComparison.OrdinalIgnoreCase)) { edge ??= p; continue; }
            return p;
        }
        return edge;
    }
}
