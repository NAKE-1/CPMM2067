using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPMM2067.App.Services;
using CPMM2067.Core;
using CPMM2067.Update;
using Microsoft.Extensions.DependencyInjection;

namespace CPMM2067.App.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    [ObservableProperty] private string _version;
    [ObservableProperty] private string _buildDate;
    [ObservableProperty] private string _dataDir = AppPaths.AppData;
    [ObservableProperty] private string _updateStatus = string.Empty;

    public string ProjectUrl { get; } = "https://github.com/cpmm2067/cpmm2067";

    public AboutViewModel()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        _version = asm.GetName().Version?.ToString(3) ?? "0.1.0";
        try
        {
            var path = Environment.ProcessPath ?? asm.Location;
            _buildDate = System.IO.File.GetLastWriteTime(path).ToString("yyyy-MM-dd hh:mm tt");
        }
        catch { _buildDate = "—"; }
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        UpdateStatus = "Checking GitHub Releases for a newer version…";
        var updater = AppHost.Services.GetRequiredService<UpdaterService>();
        var applied = await updater.CheckAndApplyAsync();
        UpdateStatus = applied
            ? "Update downloaded — relaunch CPMM2067 to apply."
            : "No update available (or app not running from an installed Velopack location yet).";
    }

    [RelayCommand]
    private void OpenProjectUrl() => Services.BrowserLauncher.OpenUrl(ProjectUrl);

    [RelayCommand]
    private void OpenDataDir()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.AppData) { UseShellExecute = true }); }
        catch { }
    }
}
