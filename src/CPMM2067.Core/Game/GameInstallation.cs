using System.IO;

namespace CPMM2067.Core.Game;

public sealed record GameInstallation
{
    public required string InstallDir { get; init; }
    public required GameStorefront Storefront { get; init; }
    public required GameVersion Version { get; init; }
    public string? StorefrontAppId { get; init; }
    public bool RedModInstalled { get; init; }

    public string ExePath => Path.Combine(InstallDir, "bin", "x64", "Cyberpunk2077.exe");
    public string ModsDir => Path.Combine(InstallDir, "mods");
    public string ArchiveModDir => Path.Combine(InstallDir, "archive", "pc", "mod");
    public string Red4extPluginsDir => Path.Combine(InstallDir, "red4ext", "plugins");
    public string CetModsDir => Path.Combine(InstallDir, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods");
    public string R6ScriptsDir => Path.Combine(InstallDir, "r6", "scripts");
    public string R6TweaksDir => Path.Combine(InstallDir, "r6", "tweaks");
    public string R6ConfigDir => Path.Combine(InstallDir, "r6", "config");
    public string RedModExePath => Path.Combine(InstallDir, "tools", "redmod", "bin", "redMod.exe");
    public string ModsJsonPath => Path.Combine(InstallDir, "tools", "redmod", "mods.json");
}
