using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPMM2067.App.Services;
using CPMM2067.Frameworks;

namespace CPMM2067.App.ViewModels;

public partial class DownloadsViewModel : ViewModelBase
{
    private readonly InstallJournal _journal;
    private readonly ModInstaller _installer;
    private readonly GameStateService _state;
    private readonly InstallQueue _queue;

    [ObservableProperty] private string _status = string.Empty;

    public ObservableCollection<JournalRow> Entries { get; } = new();
    public ObservableCollection<InstallJob> ActiveJobs { get; } = new();
    public ObservableCollection<InstallJob> AllJobs { get; }

    public DownloadsViewModel(InstallJournal journal, ModInstaller installer, GameStateService state, InstallQueue queue, NxmRouter router)
    {
        _journal = journal;
        _installer = installer;
        _state = state;
        _queue = queue;
        AllJobs = _queue.Jobs;
        _queue.Jobs.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (InstallJob j in e.NewItems)
                    j.PropertyChanged += OnJobPropertyChanged;
            RefreshActive();
        };
        foreach (var j in _queue.Jobs) j.PropertyChanged += OnJobPropertyChanged;
        RefreshActive();
        Refresh();

        // Auto-refresh history when an NXM download completes
        router.DownloadCompleted += _ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
    }

    private void OnJobPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshActive);
    }

    /// <summary>
    /// Active list shows only in-flight work. Terminal jobs (Done/DryRun/Cancelled/Failed)
    /// drop out — the journal/History panel is where finished items live.
    /// </summary>
    private void RefreshActive()
    {
        ActiveJobs.Clear();
        foreach (var j in _queue.Jobs)
            if (j.IsActive) ActiveJobs.Add(j);
    }

    [RelayCommand]
    private void Refresh()
    {
        Entries.Clear();
        foreach (var (path, entry) in _journal.LoadAll())
            Entries.Add(new JournalRow(this, path, entry));
        Status = Entries.Count == 0
            ? "No install records yet. Install something to populate this list."
            : $"{Entries.Count} record(s).";
    }

    [RelayCommand]
    private void CancelJob(InstallJob? job)
    {
        if (job == null) return;
        job.Cancel();
        Status = $"Cancelled {job.Name}.";
    }

    [RelayCommand]
    private void OpenJournalFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", InstallJournal.JournalDir) { UseShellExecute = true }); }
        catch { }
    }

    [RelayCommand]
    private async Task InstallAllDownloadedAsync()
    {
        var game = _state.Current;
        if (game == null) { Status = "No game set."; return; }
        var pending = Entries.Where(e => e.CanInstall).ToList();
        if (pending.Count == 0) { Status = "Nothing to install."; return; }
        Status = $"Installing {pending.Count} downloaded archive(s)…";
        foreach (var row in pending)
        {
            await InstallAsync(row);
        }
        Status = $"Done. {pending.Count} archive(s) processed.";
    }

    public async Task RevertAsync(JournalRow row)
    {
        var game = _state.Current;
        if (game == null) { Status = "No game set."; return; }

        var preview = BuildRevertPreview(row, game.InstallDir);
        var window = MainWindowAccessor.Get();
        if (window != null)
        {
            var result = await Views.ConfirmDialog.ShowAsync(
                window,
                title: "CPMM2067 — confirm revert",
                headline: $"[ REVERT // {row.Name} ]",
                body: preview,
                primaryLabel: "[ REVERT NOW ]");
            if (result != Views.ConfirmResult.Primary) { Status = "Revert cancelled."; return; }
        }

        Status = $"Reverting {row.Name}…";
        var ok = await _installer.RevertAsync(row.Entry, row.JournalPath, game);
        Status = ok ? $"Reverted {row.Name}." : $"Revert failed for {row.Name} — see logs.";
        Refresh();

        var w = MainWindowAccessor.Get();
        if (w != null)
        {
            await Views.ConfirmDialog.ShowResultAsync(
                w,
                title: "CPMM2067 — revert complete",
                headline: ok ? $"[ ✓ REVERTED // {row.Name} ]" : $"[ ✗ REVERT FAILED // {row.Name} ]",
                body: ok
                    ? $"Removed {row.Entry.RelativePaths.Count} file(s) from {game.InstallDir}.\nJournal entry marked Reverted."
                    : $"Could not revert — see Logs → APP LIVE TAIL for details.");
        }
    }

    public async Task InstallAsync(JournalRow row)
    {
        var game = _state.Current;
        if (game == null) { Status = "No game set."; return; }
        if (!File.Exists(row.Entry.SourceArchivePath))
        {
            Status = $"Source archive missing: {row.Entry.SourceArchivePath}";
            return;
        }

        var preview = BuildInstallPreview(row, game.InstallDir);
        var window = MainWindowAccessor.Get();
        if (window != null)
        {
            var result = await Views.ConfirmDialog.ShowAsync(
                window,
                title: "CPMM2067 — confirm install",
                headline: $"[ INSTALL // {row.Name} ]",
                body: preview,
                primaryLabel: "[ INSTALL — OVERWRITE DUPLICATES ]");
            if (result != Views.ConfirmResult.Primary) { Status = "Install cancelled."; return; }
        }

        Status = $"Installing {row.Name}…";
        var result2 = await _installer.InstallFromArchiveAsync(row.Entry.SourceArchivePath, game, dryRun: false);
        Status = result2.Message;
        Refresh();

        var w = MainWindowAccessor.Get();
        if (w != null)
        {
            var ok = result2.Ok;
            await Views.ConfirmDialog.ShowResultAsync(
                w,
                title: "CPMM2067 — install complete",
                headline: ok ? $"[ ✓ INSTALLED // {row.Name} ]" : $"[ ✗ INSTALL FAILED // {row.Name} ]",
                body: result2.Message + (ok && result2.State != null
                    ? $"\nFramework: {result2.State.Manifest.Framework}\nFiles written: {result2.State.Files.Count}"
                    : ""));
        }
    }

    private static string BuildInstallPreview(JournalRow row, string gameDir)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"source archive : {row.Entry.SourceArchivePath}");
        sb.AppendLine($"target install : {gameDir}");
        if (row.Entry.DependenciesDetected.Count > 0)
            sb.AppendLine($"dependencies   : {string.Join(", ", row.Entry.DependenciesDetected)}");
        sb.AppendLine();
        sb.AppendLine("Detected layout (resolved at install time, not pre-extraction):");
        sb.AppendLine("  • If REDmod  → files copied to mods\\<name>\\ + mods.json updated");
        sb.AppendLine("  • If RED4ext → files copied into red4ext\\, bin\\, r6\\ subdirs");
        sb.AppendLine("  • If legacy  → *.archive copied into archive\\pc\\mod\\");
        sb.AppendLine();
        sb.AppendLine("Duplicates policy: existing files at target paths will be OVERWRITTEN.");
        sb.AppendLine("(Vanilla files are shadow-copied to the backup store first when applicable.)");
        sb.AppendLine();
        sb.AppendLine("On success: a new journal entry is written with the full file list.");
        sb.AppendLine("Revert is available afterwards from this Downloads page.");
        return sb.ToString();
    }

    private static string BuildRevertPreview(JournalRow row, string gameDir)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"target install : {gameDir}");
        sb.AppendLine($"journal entry  : {row.JournalPath}");
        sb.AppendLine($"file count     : {row.Entry.RelativePaths.Count}");
        sb.AppendLine();
        sb.AppendLine("The following files will be DELETED from the install:");
        var max = System.Math.Min(50, row.Entry.RelativePaths.Count);
        for (var i = 0; i < max; i++)
            sb.AppendLine("  - " + row.Entry.RelativePaths[i]);
        if (row.Entry.RelativePaths.Count > 50)
            sb.AppendLine($"  …and {row.Entry.RelativePaths.Count - 50} more");
        sb.AppendLine();
        sb.AppendLine("Empty parent directories will be pruned afterwards.");
        sb.AppendLine("If the file overwrote a vanilla one, the backup store will restore the original.");
        sb.AppendLine();
        sb.AppendLine("Journal entry will be marked Reverted (kept for history).");
        return sb.ToString();
    }
}

public sealed partial class JournalRow : ObservableObject
{
    private readonly DownloadsViewModel _parent;
    public string JournalPath { get; }
    public InstallEntry Entry { get; }
    public string Name => Entry.Name;
    public string Version => Entry.Version ?? "—";
    public string Framework => Entry.Framework.ToString();
    public string At => Entry.At.LocalDateTime.ToString("yyyy-MM-dd hh:mm tt");
    public string StatusText => Entry.Status.ToString();
    public int FileCount => Entry.RelativePaths.Count;
    public string Dependencies => Entry.DependenciesDetected.Count == 0
        ? "—" : string.Join(", ", Entry.DependenciesDetected);

    [ObservableProperty] private bool _confirmingRevert;

    public JournalRow(DownloadsViewModel parent, string journalPath, InstallEntry entry)
    {
        _parent = parent;
        JournalPath = journalPath;
        Entry = entry;
    }

    public bool CanRevert => Entry.Status == InstallEntryStatus.Installed;
    public bool CanInstall => Entry.Status == InstallEntryStatus.Downloaded
                              && !string.IsNullOrEmpty(Entry.SourceArchivePath)
                              && File.Exists(Entry.SourceArchivePath);

    [RelayCommand] private void RequestRevert() => ConfirmingRevert = CanRevert;
    [RelayCommand] private void CancelRevert() => ConfirmingRevert = false;
    [RelayCommand] private async Task ConfirmRevertAsync()
    {
        ConfirmingRevert = false;
        await _parent.RevertAsync(this);
    }

    [RelayCommand] private async Task InstallAsync() => await _parent.InstallAsync(this);

    [RelayCommand]
    private void OpenFolder()
    {
        var path = Entry.SourceArchivePath;
        try
        {
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return;
            }
            var parent = string.IsNullOrEmpty(path) ? null : System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && System.IO.Directory.Exists(parent))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", parent) { UseShellExecute = true });
                return;
            }
            // last-resort fallback: cache dir
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", CPMM2067.Core.AppPaths.ArchiveCacheDir) { UseShellExecute = true });
        }
        catch { }
    }
}
