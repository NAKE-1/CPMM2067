using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPMM2067.App.Services;
using CPMM2067.Core;
using CPMM2067.Diagnostics;

namespace CPMM2067.App.ViewModels;

public partial class LogsViewModel : ViewModelBase
{
    private readonly GameStateService _state;
    private readonly GameLogReader _reader;

    [ObservableProperty] private string _bundlePath = string.Empty;
    [ObservableProperty] private string _bundleStatus = string.Empty;
    [ObservableProperty] private string _gameLogStatus = "no install set";
    [ObservableProperty] private GameLogFile? _selectedGameLog;
    [ObservableProperty] private string _selectedGameLogContent = string.Empty;

    public ObservableCollection<LogLine> Lines { get; } = new();
    public ObservableCollection<GameLogFile> GameLogs { get; } = new();

    public LogsViewModel(GameStateService state, GameLogReader reader)
    {
        _state = state;
        _reader = reader;

        foreach (var line in LoggingBootstrap.InMemorySink.Snapshot()) Lines.Add(line);
        LoggingBootstrap.InMemorySink.OnEmit += line =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                Lines.Add(line);
                while (Lines.Count > 500) Lines.RemoveAt(0);
            });
        };

        RefreshGameLogs();
    }

    [RelayCommand]
    private async Task CreateBundleAsync()
    {
        BundleStatus = "Building diagnostic bundle…";
        var path = await DiagnosticBundle.CreateAsync(AppPaths.AppData, _state.Current);
        BundlePath = path;
        BundleStatus = $"Saved to {path}";
    }

    [RelayCommand]
    private void RefreshGameLogs()
    {
        GameLogs.Clear();
        var game = _state.Current;
        if (game == null) { GameLogStatus = "no install set — pick a folder on Dashboard first"; return; }
        foreach (var f in _reader.Enumerate(game)) GameLogs.Add(f);
        GameLogStatus = $"{GameLogs.Count} log file(s) under {game.InstallDir}";
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        var game = _state.Current;
        if (game == null) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", game.InstallDir) { UseShellExecute = true }); }
        catch { }
    }

    partial void OnSelectedGameLogChanged(GameLogFile? value)
    {
        if (value == null) { SelectedGameLogContent = string.Empty; return; }
        _ = LoadSelectedAsync(value);
    }

    private async Task LoadSelectedAsync(GameLogFile f)
    {
        try
        {
            SelectedGameLogContent = await _reader.ReadTailAsync(f.AbsolutePath);
        }
        catch (Exception ex)
        {
            SelectedGameLogContent = $"(failed to read: {ex.Message})";
        }
    }
}
