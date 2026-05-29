using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace CPMM2067.Update;

public sealed class UpdaterService
{
    public const string DefaultReleaseFeedUrl = "https://github.com/cpmm2067/cpmm2067/releases";

    private readonly ILogger<UpdaterService> _log;

    public UpdaterService(ILogger<UpdaterService> log) => _log = log;

    public static void EarlyInit(string[] args)
    {
        VelopackApp.Build().Run();
    }

    public async Task<bool> CheckAndApplyAsync(string? releaseFeedUrl = null)
    {
        var url = releaseFeedUrl ?? DefaultReleaseFeedUrl;
        try
        {
            var mgr = new UpdateManager(new GithubSource(url, accessToken: null, prerelease: false));
            if (!mgr.IsInstalled)
            {
                _log.LogDebug("Not running from an installed location; skipping self-update");
                return false;
            }
            var info = await mgr.CheckForUpdatesAsync();
            if (info == null)
            {
                _log.LogInformation("No update available");
                return false;
            }
            _log.LogInformation("Update available: {Version}", info.TargetFullRelease.Version);
            await mgr.DownloadUpdatesAsync(info);
            mgr.ApplyUpdatesAndRestart(info);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Self-update failed");
            return false;
        }
    }
}
