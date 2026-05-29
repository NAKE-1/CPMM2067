using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CPMM2067.App.Services;
using CPMM2067.App.ViewModels;
using CPMM2067.App.Views;
using CPMM2067.Nexus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace CPMM2067.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppHost.Initialize();
        AppHost.Services.GetRequiredService<GameProcessMonitor>().Start();

        ApplyStoredApiKey();
        AutoRegisterNxmHandler();

        var router = AppHost.Services.GetRequiredService<NxmRouter>();

        // Listener for nxm URIs forwarded from secondary instances
        SingleInstance.StartListener(msg =>
        {
            if (msg.StartsWith("nxm:nxm://", System.StringComparison.OrdinalIgnoreCase))
                _ = router.HandleAsync(msg.Substring("nxm:".Length));
        }, System.Threading.CancellationToken.None);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = AppHost.Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = vm };
        }

        // Tray icon for true background mode (Show / Hide / Quit menu)
        try { AppHost.Services.GetRequiredService<TrayService>().Install(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Tray install failed: {ex.Message}"); }

        if (!string.IsNullOrEmpty(Program.PendingNxmUri))
            _ = router.HandleAsync(Program.PendingNxmUri);

        base.OnFrameworkInitializationCompleted();
    }

    private static void ApplyStoredApiKey()
    {
        var key = AppHost.Settings.NexusApiKey;
        if (string.IsNullOrWhiteSpace(key)) return;
        var api = AppHost.Services.GetRequiredService<NexusApiClient>();
        api.SetApiKey(key.Trim());
    }

    private static void AutoRegisterNxmHandler()
    {
        if (!OperatingSystem.IsWindows()) return;
        var nxm = AppHost.Services.GetService<NxmProtocolHandler>();
        if (nxm == null) return;

        var exe = ResolveExePath();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return;
        if (nxm.IsRegisteredForUs(exe)) return;
        try { nxm.Register(exe); } catch { /* best-effort */ }
    }

    private static string ResolveExePath()
    {
        var pp = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(pp) && pp.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return pp;
        var candidate = Path.Combine(AppContext.BaseDirectory, "CPMM2067.App.exe");
        return File.Exists(candidate) ? candidate : pp ?? candidate;
    }
}
