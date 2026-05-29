using System.Threading.Tasks;
using CPMM2067.Core.Game;
using CPMM2067.Frameworks;

namespace CPMM2067.App.Services;

public sealed class AlertService
{
    private readonly ModScanner _scanner;

    public AlertService(ModScanner scanner) => _scanner = scanner;

    /// <summary>
    /// If the current game version differs from the last-known one in settings, pop a modal listing
    /// the change and every scanned mod (because any of them may need re-checking after a patch).
    /// Updates settings so the alert fires once per version.
    /// </summary>
    public async Task ShowGameVersionChangedIfNeededAsync(GameInstallation game)
    {
        var current = game.Version.ToString();
        var last = AppHost.Settings.LastKnownGameVersion;

        if (string.IsNullOrEmpty(current) || current == "unknown") return;

        if (last == current)
            return;

        // Persist immediately so the modal doesn't re-fire on subsequent launches with the same version.
        AppHost.UpdateSettings(AppHost.Settings with { LastKnownGameVersion = current });

        if (string.IsNullOrEmpty(last))
        {
            // First time we ever saw this game install — no need to alert, just record.
            return;
        }

        var window = MainWindowAccessor.Get();
        if (window == null) return;

        var found = await _scanner.ScanAsync(game);
        var modList = found.Count == 0
            ? "  (no mods scanned yet — they'll show up here on next launch)"
            : string.Join("\n", System.Linq.Enumerable.Select(found,
                m => $"  • {m.Framework,-14} {m.Name}" + (string.IsNullOrEmpty(m.Version) ? "" : $"  v{m.Version}")));

        var body =
            $"Last seen   : {last}\n" +
            $"Now running : {current}\n\n" +
            "Cyberpunk's content/scripting can break across patches. The mods below were detected\n" +
            "on disk — review each for an update before [ DEPLOY + PLAY ].\n\n" +
            "Suggested checks:\n" +
            "  • REDmod          → re-deploy from Dashboard after re-checking compat\n" +
            "  • RED4ext/CET     → bump the loader and its plugins (TweakXL, ArchiveXL, Codeware)\n" +
            "  • redscript       → recompile by launching the game once with -modded\n" +
            "  • legacy .archive → usually patch-safe unless the file format changed\n\n" +
            "On disk:\n" + modList;

        await Views.ConfirmDialog.ShowResultAsync(
            window,
            title: "CPMM2067 — game version changed",
            headline: $"[ ! GAME VERSION CHANGED // {last} → {current} ]",
            body: body);
    }
}
