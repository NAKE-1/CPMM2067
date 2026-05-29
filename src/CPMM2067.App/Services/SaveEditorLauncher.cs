using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CPMM2067.App.Services;

/// <summary>
/// Launches an external CR2W-aware save editor (CyberCAT-SimpleGUI by default) with a save file
/// path as argument. We don't ship our own save-format parser — the format is partially
/// reverse-engineered and the existing community tool already does a thorough job.
/// </summary>
public static class SaveEditorLauncher
{
    public const string DefaultNexusUrl = "https://www.nexusmods.com/cyberpunk2077/mods/718";

    public static bool TryLaunch(string saveFolderPath, out string error)
    {
        error = "";
        var exe = AppHost.Settings.SaveEditorExe;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            error = "Save editor exe not configured. Settings → [ SAVE EDITOR ] → [ auto-detect ] or [ pick exe… ].";
            return false;
        }
        // CyberCAT-SimpleGUI accepts a sav.dat path as the first arg; if a folder is given, pick the sav.dat inside.
        var arg = saveFolderPath;
        if (Directory.Exists(saveFolderPath))
        {
            var sav = Path.Combine(saveFolderPath, "sav.dat");
            if (File.Exists(sav)) arg = sav;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = string.IsNullOrEmpty(arg) ? "" : '"' + arg + '"',
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
            });
            return true;
        }
        catch (Exception ex)
        {
            error = "Launch failed: " + ex.Message;
            return false;
        }
    }

    private static readonly string[] s_candidatePaths = new[]
    {
        @"%LOCALAPPDATA%\Programs\CyberCAT-SimpleGUI\CP2077SaveEditor.exe",
        @"%LOCALAPPDATA%\Programs\CP2077SaveEditor\CP2077SaveEditor.exe",
        @"%USERPROFILE%\Downloads\CyberCAT-SimpleGUI\CP2077SaveEditor.exe",
        @"%USERPROFILE%\Downloads\CP2077SaveEditor\CP2077SaveEditor.exe",
        @"C:\Tools\CyberCAT-SimpleGUI\CP2077SaveEditor.exe",
        @"C:\Tools\CP2077SaveEditor\CP2077SaveEditor.exe",
        @"C:\Program Files\CyberCAT-SimpleGUI\CP2077SaveEditor.exe",
    };

    public static string? AutoDetect()
    {
        // First: our own install location from CyberCatInstaller
        var installRoot = CyberCatInstaller.InstallRoot;
        if (Directory.Exists(installRoot))
        {
            var ours = Directory.EnumerateFiles(installRoot, "CP2077SaveEditor.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (ours != null) return ours;
        }
        // Then: common install paths the user might have hand-placed
        foreach (var raw in s_candidatePaths)
        {
            var p = Environment.ExpandEnvironmentVariables(raw);
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
