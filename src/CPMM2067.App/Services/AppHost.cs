using System;
using System.Collections.Generic;
using CPMM2067.App.ViewModels;
using CPMM2067.Backup;
using CPMM2067.Compat;
using CPMM2067.Core.Backups;
using CPMM2067.Diagnostics;
using CPMM2067.Frameworks;
using CPMM2067.Frameworks.RedMod;
using CPMM2067.GameDetect;
using CPMM2067.Launch;
using CPMM2067.Nexus;
using CPMM2067.Update;
using Microsoft.Extensions.DependencyInjection;

namespace CPMM2067.App.Services;

public static class AppHost
{
    public static IServiceProvider Services { get; private set; } = default!;

    public static AppSettings Settings { get; private set; } = new();
    public static IReadOnlyDictionary<string, string> Env { get; private set; } = new Dictionary<string, string>();

    public static void Initialize()
    {
        if (Services is not null) return;
        var loggerFactory = LoggingBootstrap.Initialize();

        Env = EnvFile.LoadFromNearestDotEnv();
        Settings = SettingsStore.Load();
        if (string.IsNullOrWhiteSpace(Settings.NexusApiKey) && Env.TryGetValue("NEXUS_API_KEY", out var envKey))
            Settings = Settings with { NexusApiKey = envKey };

        // PreferredBrowserExe stays empty until the user explicitly sets it
        // via Settings → [ auto-detect ] / [ pick exe… ]. Empty = fall back to system default.

        var sc = new ServiceCollection();
        sc.AddSingleton(loggerFactory);
        sc.AddLogging();

        sc.AddSingleton<GameStateService>();
        sc.AddSingleton<GameProcessMonitor>();
        sc.AddSingleton<GameDetector>();
        sc.AddSingleton<ModScanner>();
        sc.AddSingleton<IBackupStore, FileBackupStore>();
        sc.AddSingleton<SavesSnapshotter>();
        sc.AddSingleton<RedModHandler>();
        sc.AddSingleton<CPMM2067.Frameworks.LegacyArchive.LegacyArchiveHandler>();
        sc.AddSingleton<CPMM2067.Frameworks.Red4ext.Red4extHandler>();
        sc.AddSingleton<CPMM2067.Frameworks.TweakXL.TweakXLHandler>();
        sc.AddSingleton<CPMM2067.Frameworks.Cet.CetHandler>();
        sc.AddSingleton<CPMM2067.Frameworks.Redscript.RedscriptHandler>();
        sc.AddSingleton<CPMM2067.Frameworks.Fomod.FomodHandler>();
        sc.AddSingleton<CPMM2067.Frameworks.Fomod.IFomodChooser, WizardFomodChooser>();
        sc.AddSingleton<CPMM2067.Archives.ArchiveExtractor>();
        sc.AddSingleton<CPMM2067.Frameworks.InstallJournal>();
        sc.AddSingleton<CPMM2067.Frameworks.InstallQueue>();
        sc.AddSingleton<CPMM2067.Frameworks.ModInstaller>();
        sc.AddSingleton<CompatEngine>();
        sc.AddSingleton<NexusRateLimitTracker>();
        sc.AddSingleton<NexusApiClient>();
        sc.AddSingleton<NxmDownloadService>();
        sc.AddSingleton<NexusCollectionsClient>();
        sc.AddSingleton<NxmRouter>();
        if (OperatingSystem.IsWindows())
            sc.AddSingleton<NxmProtocolHandler>();
        sc.AddSingleton<GameLauncher>();
        sc.AddSingleton<CPMM2067.Diagnostics.GameLogReader>();
        sc.AddSingleton<UpdaterService>();
        sc.AddSingleton<TrayService>();
        sc.AddSingleton<AlertService>();

        sc.AddTransient<MainWindowViewModel>();
        sc.AddTransient<DashboardViewModel>();
        sc.AddSingleton<ModListViewModel>();
        sc.AddTransient<DownloadsViewModel>();
        sc.AddTransient<LoadOrderViewModel>();
        sc.AddTransient<SettingsViewModel>();
        sc.AddTransient<LogsViewModel>();
        sc.AddTransient<AboutViewModel>();
        sc.AddTransient<SavesViewModel>();
        sc.AddTransient<ConflictsViewModel>();
        sc.AddSingleton<CollectionsViewModel>();
        sc.AddTransient<LoadReportViewModel>();

        Services = sc.BuildServiceProvider();
    }

    public static void UpdateSettings(AppSettings settings)
    {
        Settings = settings;
        SettingsStore.Save(settings);
    }
}
