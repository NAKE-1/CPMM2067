using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPMM2067.Nexus;

namespace CPMM2067.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly NexusApiClient _nexus;
    private readonly NxmProtocolHandler? _nxm;

    [ObservableProperty] private string _nexusApiKey = string.Empty;
    [ObservableProperty] private string _nexusJwt = string.Empty;
    [ObservableProperty] private string _nexusStatus = "Not validated";
    [ObservableProperty] private bool _telemetryEnabled;
    [ObservableProperty] private bool _testingMode;
    [ObservableProperty] private bool _autoScanOnStartup;
    [ObservableProperty] private string _manualGamePath = string.Empty;
    [ObservableProperty] private string _nxmHandlerStatus = "Unknown";
    [ObservableProperty] private string _preferredBrowserExe = string.Empty;

    public NexusRateLimitTracker RateLimits { get; }

    public SettingsViewModel(NexusApiClient nexus, NexusRateLimitTracker tracker)
    {
        _nexus = nexus;
        RateLimits = tracker;
        if (OperatingSystem.IsWindows())
            _nxm = CPMM2067.App.Services.AppHost.Services.GetService(typeof(NxmProtocolHandler)) as NxmProtocolHandler;

        var s = CPMM2067.App.Services.AppHost.Settings;
        _nexusApiKey = s.NexusApiKey ?? string.Empty;
        _nexusJwt = s.NexusJwt ?? string.Empty;
        _telemetryEnabled = s.TelemetryEnabled;
        _testingMode = s.TestingMode;
        _autoScanOnStartup = s.AutoScanOnStartup;
        _preferredBrowserExe = s.PreferredBrowserExe ?? string.Empty;
        if (CPMM2067.App.Services.AppHost.Env.ContainsKey("NEXUS_API_KEY"))
            _nexusStatus = "Loaded from .env";

        RefreshNxmStatus();
    }

    partial void OnTelemetryEnabledChanged(bool value)
    {
        var s = CPMM2067.App.Services.AppHost.Settings with { TelemetryEnabled = value };
        CPMM2067.App.Services.AppHost.UpdateSettings(s);
    }

    partial void OnTestingModeChanged(bool value)
    {
        var s = CPMM2067.App.Services.AppHost.Settings with { TestingMode = value };
        CPMM2067.App.Services.AppHost.UpdateSettings(s);
    }

    partial void OnAutoScanOnStartupChanged(bool value)
    {
        var s = CPMM2067.App.Services.AppHost.Settings with { AutoScanOnStartup = value };
        CPMM2067.App.Services.AppHost.UpdateSettings(s);
    }

    partial void OnNexusJwtChanged(string value)
    {
        var s = CPMM2067.App.Services.AppHost.Settings with { NexusJwt = string.IsNullOrWhiteSpace(value) ? null : value.Trim() };
        CPMM2067.App.Services.AppHost.UpdateSettings(s);
    }

    partial void OnPreferredBrowserExeChanged(string value)
    {
        var s = CPMM2067.App.Services.AppHost.Settings with
        {
            PreferredBrowserExe = string.IsNullOrWhiteSpace(value) ? null : value.Trim()
        };
        CPMM2067.App.Services.AppHost.UpdateSettings(s);
    }

    [RelayCommand]
    private void AutoDetectBrowser()
    {
        var found = CPMM2067.App.Services.BrowserLauncher.AutoDetect();
        if (!string.IsNullOrEmpty(found)) PreferredBrowserExe = found;
    }

    [RelayCommand]
    private void TestBrowser()
        => CPMM2067.App.Services.BrowserLauncher.OpenUrl("https://example.com/");

    [RelayCommand]
    private async System.Threading.Tasks.Task PickBrowserExeAsync()
    {
        var window = CPMM2067.App.Services.MainWindowAccessor.Get();
        if (window?.StorageProvider == null) return;
        var picked = await window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Pick your browser .exe",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Programs") { Patterns = new[] { "*.exe" } },
            },
        });
        if (picked.Count > 0) PreferredBrowserExe = picked[0].Path.LocalPath;
    }

    [RelayCommand]
    private async Task ValidateNexusAsync()
    {
        if (string.IsNullOrWhiteSpace(NexusApiKey)) { NexusStatus = "Enter an API key first"; return; }
        try
        {
            var key = NexusApiKey.Trim();
            _nexus.SetApiKey(key);
            var info = await _nexus.ValidateKeyAsync();
            if (info == null) { NexusStatus = "Invalid key"; return; }
            NexusStatus = $"OK — {info.Name} ({(info.IsPremium ? "premium" : "standard")})";

            var s = CPMM2067.App.Services.AppHost.Settings with { NexusApiKey = key };
            CPMM2067.App.Services.AppHost.UpdateSettings(s);
        }
        catch (Exception ex)
        {
            NexusStatus = $"Error: {ex.Message}";
        }
    }

    private static string ResolveExePath()
    {
        var pp = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(pp) && pp.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return pp;
        var candidate = Path.Combine(AppContext.BaseDirectory, "CPMM2067.App.exe");
        if (File.Exists(candidate)) return candidate;
        return pp ?? candidate;
    }

    [RelayCommand]
    private void RegisterNxmHandler()
    {
        if (_nxm == null || !OperatingSystem.IsWindows()) { NxmHandlerStatus = "Windows only"; return; }
        var exe = ResolveExePath();
        try
        {
            _nxm.Register(exe);
            RefreshNxmStatus();
            if (NxmHandlerStatus != "Registered")
                NxmHandlerStatus = "Wrote registry, but verify failed — try as admin if other tools also handle nxm://";
        }
        catch (Exception ex)
        {
            NxmHandlerStatus = $"Register failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void UnregisterNxmHandler()
    {
        if (_nxm == null || !OperatingSystem.IsWindows()) return;
        try
        {
            _nxm.Unregister();
            RefreshNxmStatus();
        }
        catch (Exception ex)
        {
            NxmHandlerStatus = $"Unregister failed: {ex.Message}";
        }
    }

    private void RefreshNxmStatus()
    {
        if (_nxm == null || !OperatingSystem.IsWindows()) { NxmHandlerStatus = "Windows only"; return; }
        var exe = ResolveExePath();
        NxmHandlerStatus = _nxm.IsRegisteredForUs(exe) ? "Registered" : "Not registered";
    }
}
