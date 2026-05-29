using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CPMM2067.Frameworks;

public enum InstallJobStatus
{
    Queued,
    Downloading,
    Extracting,
    Detecting,
    Installing,
    Done,
    DryRun,
    Failed,
    Cancelled,
}

public sealed class InstallJob : INotifyPropertyChanged
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string SourcePath { get; }
    public DateTime StartedAt { get; } = DateTime.UtcNow;
    public string? TestFolder { get; set; }
    public CancellationTokenSource Cts { get; } = new();

    private string _name;
    public string Name { get => _name; set => Set(ref _name, value); }

    private InstallJobStatus _status = InstallJobStatus.Queued;
    public InstallJobStatus Status { get => _status; set => Set(ref _status, value); }

    private string _statusText = "queued";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private int _progress = -1;     // -1 == indeterminate
    public int ProgressPercent { get => _progress; set => Set(ref _progress, value); }

    private double _speedMBps;
    public double SpeedMBps { get => _speedMBps; set => Set(ref _speedMBps, value); }

    private TimeSpan? _etaRemaining;
    public TimeSpan? EtaRemaining { get => _etaRemaining; set => Set(ref _etaRemaining, value); }

    private long _bytesReceived;
    public long BytesReceived { get => _bytesReceived; set => Set(ref _bytesReceived, value); }

    private long _bytesTotal;
    public long BytesTotal { get => _bytesTotal; set => Set(ref _bytesTotal, value); }

    public bool HasDeterminateProgress => _progress >= 0;

    private string? _resultMessage;
    public string? ResultMessage { get => _resultMessage; set => Set(ref _resultMessage, value); }

    public bool IsTerminal => Status is InstallJobStatus.Done
                                       or InstallJobStatus.DryRun
                                       or InstallJobStatus.Failed
                                       or InstallJobStatus.Cancelled;
    public bool IsActive => !IsTerminal;
    public bool CanCancel => IsActive;
    public bool IsCancelled => Status == InstallJobStatus.Cancelled;
    public bool IsFailed => Status == InstallJobStatus.Failed;
    public bool BarShouldAnimate => !IsTerminal && !HasDeterminateProgress;

    public InstallJob(string sourcePath, string? displayName = null)
    {
        SourcePath = sourcePath;
        _name = displayName ?? Path.GetFileName(sourcePath);
    }

    public void Cancel()
    {
        if (IsTerminal) return;
        try { Cts.Cancel(); } catch { }
        Status = InstallJobStatus.Cancelled;
        StatusText = "cancelled by user";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTerminal)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanCancel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCancelled)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFailed)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BarShouldAnimate)));
    }
}

public sealed class InstallQueue
{
    public ObservableCollection<InstallJob> Jobs { get; } = new();

    public InstallJob Enqueue(string sourcePath, string? displayName = null)
    {
        var job = new InstallJob(sourcePath, displayName);
        Jobs.Insert(0, job);
        while (Jobs.Count > 50) Jobs.RemoveAt(Jobs.Count - 1);
        return job;
    }

    public int ActiveCount
    {
        get
        {
            var n = 0;
            foreach (var j in Jobs) if (j.IsActive) n++;
            return n;
        }
    }

    public InstallJob? Active
    {
        get
        {
            foreach (var j in Jobs) if (j.IsActive) return j;
            return null;
        }
    }
}
