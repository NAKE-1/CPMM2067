using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CPMM2067.Core.Game;
using CPMM2067.Core.Mods;
using CPMM2067.Frameworks.RedMod;
using Microsoft.Extensions.Logging;

namespace CPMM2067.Launch;

public sealed class GameLauncher
{
    private readonly ILogger<GameLauncher> _log;
    private readonly RedModHandler _redmod;

    public GameLauncher(ILogger<GameLauncher> log, RedModHandler redmod)
    {
        _log = log;
        _redmod = redmod;
    }

    public async Task LaunchAsync(GameInstallation game, bool forceDeploy = false, CancellationToken ct = default)
    {
        if (forceDeploy || NeedsDeploy(game))
            await _redmod.DeployAsync(game, ct).ConfigureAwait(false);

        var uri = LaunchUri(game);
        if (uri == null)
        {
            _log.LogInformation("Falling back to direct exe launch: {Exe}", game.ExePath);
            Process.Start(new ProcessStartInfo
            {
                FileName = game.ExePath,
                WorkingDirectory = Path.GetDirectoryName(game.ExePath)!,
                UseShellExecute = true,
            });
            return;
        }

        _log.LogInformation("Launching via storefront URI: {Uri}", uri);
        Process.Start(new ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true,
        });
    }

    private bool NeedsDeploy(GameInstallation game)
    {
        var marker = Path.Combine(game.InstallDir, "tools", "redmod", "metadata.json");
        var modsJson = game.ModsJsonPath;
        if (!File.Exists(modsJson)) return false;
        if (!File.Exists(marker)) return true;
        return File.GetLastWriteTimeUtc(modsJson) > File.GetLastWriteTimeUtc(marker);
    }

    private static string? LaunchUri(GameInstallation game) => game.Storefront switch
    {
        GameStorefront.Steam => $"steam://run/{game.StorefrontAppId ?? "1091500"}",
        GameStorefront.Gog when !string.IsNullOrEmpty(game.StorefrontAppId)
            => $"goggalaxy://openGameView/{game.StorefrontAppId}",
        GameStorefront.Epic when !string.IsNullOrEmpty(game.StorefrontAppId)
            => $"com.epicgames.launcher://apps/{game.StorefrontAppId}?action=launch&silent=true",
        _ => null,
    };
}
