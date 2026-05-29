using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPMM2067.App.Services;
using CPMM2067.Core;
using CPMM2067.Core.Game;
using CPMM2067.GameDetect;
using CPMM2067.Launch;
using Microsoft.Extensions.DependencyInjection;

namespace CPMM2067.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly GameDetector _detector;
    private readonly GameLauncher _launcher;
    private readonly GameStateService _state;

    [ObservableProperty] private string _gameStatus = "Detecting…";
    [ObservableProperty] private string _gameVersion = "—";
    [ObservableProperty] private string _gameStorefront = "—";
    [ObservableProperty] private string _gameInstallDir = "—";
    [ObservableProperty] private bool _redModInstalled;
    [ObservableProperty] private bool _gameDetected;

    public GameInstallation? Game => _state.Current;

    public DashboardViewModel(GameDetector detector, GameLauncher launcher, GameStateService state)
    {
        _detector = detector;
        _launcher = launcher;
        _state = state;
        _ = DetectAsync();
    }

    [RelayCommand]
    private async Task DetectAsync()
    {
        GameStatus = "Detecting…";
        GameDetected = false;
        try
        {
            var savedPath = AppHost.Settings.ManualGamePath;
            if (!string.IsNullOrWhiteSpace(savedPath))
            {
                var manual = _detector.FromManualPath(savedPath);
                if (manual != null) { ApplyGame(manual); return; }
                GameStatus = $"Saved path no longer valid: {savedPath}";
            }
            var game = await _detector.DetectAsync();
            ApplyGame(game);
        }
        catch (Exception ex)
        {
            GameStatus = $"Detection failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PickFolderAsync()
    {
        var window = MainWindowHandle();
        if (window?.StorageProvider == null) return;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pick a Cyberpunk 2077 install (or a TEST FOLDER)",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;
        var path = folders[0].Path.LocalPath;
        var manual = _detector.FromManualPath(path);
        if (manual == null)
        {
            GameStatus = $"Not a valid CP2077 install: {path}";
            return;
        }
        AppHost.UpdateSettings(AppHost.Settings with { ManualGamePath = path });
        ApplyGame(manual);
    }

    [RelayCommand]
    private void ClearSavedPath()
    {
        AppHost.UpdateSettings(AppHost.Settings with { ManualGamePath = null });
        GameStatus = "Saved path cleared. Re-detect to use auto-detection.";
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        if (Game == null) return;
        AppHost.Services.GetRequiredService<GameProcessMonitor>().NotifyStarting();
        await _launcher.LaunchAsync(Game);
    }

    [RelayCommand]
    private void OpenAppData()
    {
        AppPaths.EnsureAll();
        try { Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.AppData) { UseShellExecute = true }); }
        catch { /* best effort */ }
    }

    [RelayCommand]
    private void GoToSaves()
    {
        // Hop to the Saves page via the main window VM
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d
            && d.MainWindow?.DataContext is MainWindowViewModel mw)
        {
            var saves = AppHost.Services.GetRequiredService<SavesViewModel>();
            mw.Current = saves;
        }
    }

    private void ApplyGame(GameInstallation? game)
    {
        _state.Set(game);
        if (game == null)
        {
            GameStatus = "Cyberpunk 2077 not detected — click 'Pick folder' to point at a test or install dir.";
            GameDetected = false;
            return;
        }
        GameStatus = $"Found Cyberpunk 2077 ({game.Storefront})";
        GameVersion = game.Version.ToString();
        GameStorefront = game.Storefront.ToString();
        GameInstallDir = game.InstallDir;
        RedModInstalled = game.RedModInstalled;
        GameDetected = true;
    }

    private static Window? MainWindowHandle()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}
