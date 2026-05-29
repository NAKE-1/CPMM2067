using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPMM2067.App.Services;
using CPMM2067.Frameworks;
using Microsoft.Extensions.DependencyInjection;

namespace CPMM2067.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private ViewModelBase _current;
    [ObservableProperty] private string _statusBar = "CPMM2067 v0.3 :: cyberpunk 2077 mod manager";
    [ObservableProperty] private string _gameStatusLabel = "[ GAME: NOT RUNNING ]";
    [ObservableProperty] private string _gameStatusDetail = "not running";

    [ObservableProperty] private string _queueLabel = "[ QUEUE: 0 ]";
    [ObservableProperty] private string _queueDetail = "idle";
    [ObservableProperty] private bool _hasActiveJob;
    [ObservableProperty] private int _queueProgress;
    [ObservableProperty] private bool _queueIndeterminate;

    public IReadOnlyList<NavItem> NavItems { get; }
    public InstallQueue Queue { get; }

    public MainWindowViewModel()
    {
        _current = AppHost.Services.GetRequiredService<DashboardViewModel>();

        var monitor = AppHost.Services.GetRequiredService<GameProcessMonitor>();
        monitor.Changed += OnGameStateChanged;

        Queue = AppHost.Services.GetRequiredService<InstallQueue>();
        Queue.Jobs.CollectionChanged += OnJobsChanged;

        NavItems = new[]
        {
            new NavItem("Dashboard", new RelayCommand(() => Navigate<DashboardViewModel>())),
            new NavItem("Mods", new RelayCommand(() => Navigate<ModListViewModel>())),
            new NavItem("Downloads", new RelayCommand(() => Navigate<DownloadsViewModel>())),
            new NavItem("Collections", new RelayCommand(() => Navigate<CollectionsViewModel>())),
            new NavItem("Load order", new RelayCommand(() => Navigate<LoadOrderViewModel>())),
            new NavItem("Conflicts", new RelayCommand(() => Navigate<ConflictsViewModel>())),
            new NavItem("Load report", new RelayCommand(() => Navigate<LoadReportViewModel>())),
            new NavItem("Saves", new RelayCommand(() => Navigate<SavesViewModel>())),
            new NavItem("Logs", new RelayCommand(() => Navigate<LogsViewModel>())),
            new NavItem("Settings", new RelayCommand(() => Navigate<SettingsViewModel>())),
            new NavItem("About", new RelayCommand(() => Navigate<AboutViewModel>())),
        };
    }

    private void OnGameStateChanged(GameRunState state, string text)
    {
        var label = state switch
        {
            GameRunState.NotRunning => "[ GAME: NOT RUNNING ]",
            GameRunState.Starting => "[ GAME: STARTING… ]",
            GameRunState.Running => "[ GAME: RUNNING ]",
            GameRunState.Stopping => "[ GAME: STOPPING ]",
            GameRunState.Crashed => "[ GAME: CRASHED? ]",
            _ => "[ GAME: ? ]",
        };
        Dispatcher.UIThread.Post(() =>
        {
            GameStatusLabel = label;
            GameStatusDetail = text;
        });
    }

    private void Navigate<T>() where T : ViewModelBase
        => Current = AppHost.Services.GetRequiredService<T>();

    private void OnJobsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (InstallJob j in e.NewItems)
                j.PropertyChanged += (_, __) => Dispatcher.UIThread.Post(RefreshQueueLabel);
        }
        Dispatcher.UIThread.Post(RefreshQueueLabel);
    }

    private void RefreshQueueLabel()
    {
        var active = Queue.ActiveCount;
        QueueLabel = $"[ QUEUE: {active} ]";
        var head = Queue.Active;
        QueueDetail = head == null
            ? (Queue.Jobs.Count == 0 ? "idle" : $"last: {Queue.Jobs[0].Name} — {Queue.Jobs[0].StatusText}")
            : $"{head.Name} — {head.StatusText}";
        HasActiveJob = active > 0;
        if (head != null && head.HasDeterminateProgress)
        {
            QueueProgress = head.ProgressPercent;
            QueueIndeterminate = false;
        }
        else
        {
            QueueProgress = 0;
            QueueIndeterminate = active > 0;
        }
    }

    [RelayCommand]
    private void ShowDownloads() => Navigate<DownloadsViewModel>();
}

public sealed record NavItem(string Title, RelayCommand Command);
