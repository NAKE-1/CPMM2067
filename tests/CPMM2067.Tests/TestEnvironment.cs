using System;
using System.IO;
using CPMM2067.Core.Game;

namespace CPMM2067.Tests;

internal static class TestEnvironment
{
    public static string CreateTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), "cpmm2067-tests", prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static GameInstallation BuildFakeGame(string installDir, string version = "2.21")
    {
        Directory.CreateDirectory(Path.Combine(installDir, "bin", "x64"));
        File.WriteAllText(Path.Combine(installDir, "bin", "x64", "Cyberpunk2077.exe"), "fake exe content");
        Directory.CreateDirectory(Path.Combine(installDir, "mods"));
        Directory.CreateDirectory(Path.Combine(installDir, "tools", "redmod"));
        GameVersion.TryParse(version, out var v);
        return new GameInstallation
        {
            InstallDir = installDir,
            Storefront = GameStorefront.Manual,
            Version = v,
            RedModInstalled = false,
        };
    }

    public static string BuildFakeRedModZipExtractedDir(string rootName)
    {
        var dir = CreateTempDir("redmod-src-" + rootName);
        var modDir = Path.Combine(dir, "mods", rootName);
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "info.json"),
            "{\"name\":\"" + rootName + "\",\"version\":\"1.0.0\",\"description\":\"test mod\"}");
        Directory.CreateDirectory(Path.Combine(modDir, "scripts"));
        File.WriteAllText(Path.Combine(modDir, "scripts", "main.reds"), "// stub");
        return dir;
    }
}
