using System;
using CPMM2067.Core.Game;

namespace CPMM2067.App.Services;

public sealed class GameStateService
{
    public GameInstallation? Current { get; private set; }
    public event Action<GameInstallation?>? Changed;

    public void Set(GameInstallation? game)
    {
        Current = game;
        Changed?.Invoke(game);
        if (game != null)
        {
            // Fire-and-forget the alert check; AlertService handles the modal + settings update.
            try
            {
                var alerts = AppHost.Services?.GetService(typeof(AlertService)) as AlertService;
                if (alerts != null)
                    _ = alerts.ShowGameVersionChangedIfNeededAsync(game);
            }
            catch { /* best effort — alerts are non-critical */ }
        }
    }
}
