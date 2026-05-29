using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using CPMM2067.App.Services;
using CPMM2067.Frameworks;
using Microsoft.Extensions.DependencyInjection;

namespace CPMM2067.App.Views;

public partial class MainWindow : Window
{
    private bool _confirmedClose;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_confirmedClose) return;

        var queue = AppHost.Services.GetService<InstallQueue>();
        var monitor = AppHost.Services.GetService<GameProcessMonitor>();
        var activeJobs = queue?.ActiveCount ?? 0;
        var gameBusy = monitor != null
            && (monitor.State == GameRunState.Running || monitor.State == GameRunState.Starting);

        if (activeJobs == 0 && !gameBusy) return; // normal close

        e.Cancel = true;
        await Task.Yield();

        var reasons = "";
        if (activeJobs > 0) reasons += $"  • {activeJobs} active transfer(s) in the install queue\n";
        if (gameBusy) reasons += $"  • Cyberpunk 2077 is {monitor!.State.ToString().ToLowerInvariant()}\n";

        var body =
            "Closing now will abort the items below:\n\n" +
            reasons + "\n" +
            "Pick [ HIDE TO TRAY ] to send CPMM2067 to the system tray; downloads keep running.\n" +
            "Right-click the tray icon (or pick its 'Show CPMM2067' menu item) to bring the window back.\n" +
            "Pick [ CLOSE ANYWAY ] to abort everything and exit.";

        var result = await ConfirmDialog.ShowAsync(
            this,
            title: "CPMM2067 — close while busy?",
            headline: "[ ACTIVE WORK DETECTED ]",
            body: body,
            primaryLabel: "[ HIDE TO TRAY ]",
            secondaryLabel: "[ CLOSE ANYWAY ]");

        if (result == ConfirmResult.Primary)
        {
            Hide(); // tray icon stays visible
        }
        else if (result == ConfirmResult.Secondary)
        {
            _confirmedClose = true;
            Close();
        }
    }
}
