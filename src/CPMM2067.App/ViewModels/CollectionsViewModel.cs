using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPMM2067.App.Services;
using CPMM2067.Core;
using CPMM2067.Frameworks;
using CPMM2067.Nexus;

namespace CPMM2067.App.ViewModels;

public partial class CollectionsViewModel : ViewModelBase
{
    private readonly NexusCollectionsClient _collections;
    private readonly NexusApiClient _api;
    private readonly NxmDownloadService _downloader;
    private readonly InstallQueue _queue;
    private readonly ModInstaller _installer;
    private readonly GameStateService _state;

    [ObservableProperty] private string _slugInput = string.Empty;
    [ObservableProperty] private string _status = "Paste a Nexus collection URL or slug above and click [ FETCH ].";
    [ObservableProperty] private string _collectionName = "";
    [ObservableProperty] private string _collectionSummary = "";
    [ObservableProperty] private string _collectionAuthor = "";
    [ObservableProperty] private int _collectionRevision;
    [ObservableProperty] private string _downloadRoot = DefaultRoot;
    [ObservableProperty] private bool _isLoaded;

    public ObservableCollection<CollectionModRow> Mods { get; } = new();

    public static string DefaultRoot => Path.Combine(AppPaths.AppData, "collections");

    public CollectionsViewModel(
        NexusCollectionsClient collections,
        NexusApiClient api,
        NxmDownloadService downloader,
        InstallQueue queue,
        ModInstaller installer,
        GameStateService state)
    {
        _collections = collections;
        _api = api;
        _downloader = downloader;
        _queue = queue;
        _installer = installer;
        _state = state;
    }

    [ObservableProperty] private string _lastErrorDetail = string.Empty;

    [RelayCommand]
    private async Task FetchAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SlugInput))
            {
                Status = "Enter a collection URL or slug first.";
                return;
            }
            var key = AppHost.Settings.NexusApiKey;
            var jwt = AppHost.Settings.NexusJwt;
            if (string.IsNullOrEmpty(key) && string.IsNullOrEmpty(jwt))
            {
                Status = "No Nexus API key OR JWT — set one in Settings.";
                return;
            }
            if (!string.IsNullOrEmpty(key)) _collections.SetApiKey(key);
            if (!string.IsNullOrEmpty(jwt)) _collections.SetJwt(jwt);
            if (!string.IsNullOrEmpty(key)) _api.SetApiKey(key);
            Mods.Clear();
            IsLoaded = false;
            LastErrorDetail = string.Empty;
            Status = $"Fetching collection '{NexusCollectionsClient.ExtractSlug(SlugInput)}'…";

            var result = await _collections.FetchAsync(SlugInput);
            if (!result.Ok || result.Collection == null)
            {
                Status = result.ErrorMessage ?? "Fetch failed (unknown reason).";
                LastErrorDetail = result.RawResponseBody == null
                    ? "(no response body)"
                    : (result.RawResponseBody.Length > 4000
                        ? result.RawResponseBody[..4000] + "…[truncated]"
                        : result.RawResponseBody);
                return;
            }
            var col = result.Collection;
            CollectionName = col.Name;
            CollectionSummary = col.Summary;
            CollectionAuthor = col.Author;
            CollectionRevision = col.Revision;

            foreach (var m in col.Mods)
                Mods.Add(new CollectionModRow(this, m));

            IsLoaded = true;
            Status = $"Loaded {col.Mods.Count} mod(s) from '{col.Name}' rev {col.Revision} by {col.Author}.";
        }
        catch (Exception ex)
        {
            Status = "Fetch error: " + ex.Message;
            LastErrorDetail = ex.ToString();
        }
    }

    [RelayCommand]
    private async Task LoadCollectionFileAsync()
    {
        var window = MainWindowAccessor.Get();
        if (window?.StorageProvider == null) return;
        var picked = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pick a collection manifest (collection_data.json or .collection/.zip bundle)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Collection bundles") { Patterns = new[] { "*.json", "*.collection", "*.zip" } },
                new FilePickerFileType("Any") { Patterns = new[] { "*" } },
            },
        });
        if (picked.Count == 0) return;
        var path = picked[0].Path.LocalPath;

        Mods.Clear();
        IsLoaded = false;
        LastErrorDetail = string.Empty;
        Status = $"Loading manifest from {Path.GetFileName(path)}…";

        try
        {
            var col = CollectionManifestParser.LoadFromFile(path);
            CollectionName = col.Name;
            CollectionSummary = col.Summary;
            CollectionAuthor = col.Author;
            CollectionRevision = col.Revision;
            foreach (var m in col.Mods)
                Mods.Add(new CollectionModRow(this, m));
            IsLoaded = true;
            Status = $"Loaded {col.Mods.Count} mod(s) from local manifest '{col.Name}'" +
                     (col.Revision > 0 ? $" rev {col.Revision}" : "") +
                     (string.IsNullOrEmpty(col.Author) ? "" : $" by {col.Author}") + ".";

            // Make sure the API key is set for the per-file download endpoint
            var key = AppHost.Settings.NexusApiKey;
            if (!string.IsNullOrEmpty(key)) _api.SetApiKey(key);
        }
        catch (Exception ex)
        {
            Status = $"Could not parse manifest: {ex.Message}";
            LastErrorDetail = ex.ToString();
        }
    }

    [RelayCommand]
    private async Task PickDownloadRootAsync()
    {
        var window = MainWindowAccessor.Get();
        if (window?.StorageProvider == null) return;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pick a directory to save collection downloads",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;
        DownloadRoot = folders[0].Path.LocalPath;
    }

    [RelayCommand]
    private void OpenDownloadRoot()
    {
        Directory.CreateDirectory(DownloadRoot);
        try { Process.Start(new ProcessStartInfo("explorer.exe", DownloadRoot) { UseShellExecute = true }); }
        catch { }
    }

    [RelayCommand]
    private async Task DownloadAllAsync()
    {
        try
        {
            var slug = string.IsNullOrEmpty(CollectionName) ? "collection" : CollectionName.Replace(' ', '_');
            var target = Path.Combine(DownloadRoot, $"{slug}-r{CollectionRevision}");
            Directory.CreateDirectory(target);
            var required = Mods.Where(m => !m.Optional).ToList();
            Status = $"Downloading {required.Count} required mod(s) to {target}…";
            foreach (var m in required)
            {
                await DownloadOneAsync(m, target);
            }
            Status = "Download pass complete (optional mods skipped — use per-row [ DL ] for those).";
        }
        catch (Exception ex)
        {
            Status = "Download all error: " + ex.Message;
        }
    }

    public async Task DownloadOneAsync(CollectionModRow row, string? rootOverride = null)
    {
        try
        {
            // Apply API key if needed; non-fatal if missing
            var apiKey = AppHost.Settings.NexusApiKey;
            if (!string.IsNullOrEmpty(apiKey)) _api.SetApiKey(apiKey);

            var target = rootOverride ?? Path.Combine(DownloadRoot,
                $"{CollectionName.Replace(' ', '_')}-r{CollectionRevision}");
            Directory.CreateDirectory(target);

            var job = _queue.Enqueue($"{row.ModName} — {row.FileName}", row.ModName);
            job.Status = InstallJobStatus.Downloading;
            job.StatusText = $"downloading {row.ModName} (mod #{row.ModId} / file #{row.FileId})…";

            SetRowDownloadStatus(row, "starting…");

            var dl = await _downloader.DownloadAsync(
                $"nxm://cyberpunk2077/mods/{row.ModId}/files/{row.FileId}",
                target,
                progress: (rec, tot, pct) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        job.BytesReceived = rec;
                        job.BytesTotal = tot ?? 0;
                        job.ProgressPercent = pct < 0 ? -1 : (int)pct;
                        job.StatusText = tot.HasValue
                            ? $"{rec / 1024.0 / 1024.0:F1}/{tot.Value / 1024.0 / 1024.0:F1} MB ({(int)pct}%)"
                            : $"{rec / 1024.0 / 1024.0:F1} MB";
                    });
                },
                ct: job.Cts.Token);

            if (!dl.Ok || dl.LocalPath == null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    job.Status = InstallJobStatus.Failed;
                    job.StatusText = dl.Message;
                });
                SetRowDownloadStatus(row, "FAILED: " + dl.Message);
                return;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                job.Status = InstallJobStatus.Done;
                job.StatusText = "downloaded";
            });
            Avalonia.Threading.Dispatcher.UIThread.Post(() => row.DownloadedPath = dl.LocalPath);
            SetRowDownloadStatus(row, "Downloaded");
        }
        catch (Exception ex)
        {
            SetRowDownloadStatus(row, "FAILED: " + ex.Message);
            Status = "Download error: " + ex.Message;
        }
    }

    private static void SetRowDownloadStatus(CollectionModRow row, string s)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() => row.DownloadStatus = s);

    private static void SetRowInstallStatus(CollectionModRow row, string s)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() => row.InstallStatus = s);

    [RelayCommand]
    private async Task InstallAllAsync()
    {
        try
        {
            var game = _state.Current;
            if (game == null) { Status = "No game set — pick a folder on Dashboard first."; return; }
            var rows = Mods.Where(m => !string.IsNullOrEmpty(m.DownloadedPath) && File.Exists(m.DownloadedPath)).ToList();
            if (rows.Count == 0) { Status = "Nothing to install — download mods first."; return; }
            Status = $"Installing {rows.Count} downloaded mod(s)…";
            foreach (var row in rows)
            {
                try
                {
                    SetRowInstallStatus(row, "installing…");
                    var r = await _installer.InstallFromArchiveAsync(row.DownloadedPath!, game, dryRun: false);
                    SetRowInstallStatus(row, r.Ok ? "Installed" : "FAILED: " + r.Message);
                }
                catch (Exception ex)
                {
                    SetRowInstallStatus(row, "FAILED: " + ex.Message);
                }
            }
            Status = $"Done — {rows.Count(r => r.InstallStatus == "Installed")} of {rows.Count} installed.";
            return; // skip the legacy loop below
        }
        catch (Exception ex)
        {
            Status = "Install all error: " + ex.Message;
        }
    }
}

public sealed partial class CollectionModRow : ObservableObject
{
    private readonly CollectionsViewModel _parent;
    public int ModId { get; }
    public int FileId { get; }
    public string ModName { get; }
    public string FileName { get; }
    public string Author { get; }
    public bool Optional { get; }
    public string NexusPageUrl => $"https://www.nexusmods.com/cyberpunk2077/mods/{ModId}";
    public string FilesPageUrl => $"https://www.nexusmods.com/cyberpunk2077/mods/{ModId}?tab=files&file_id={FileId}&nmm=1";

    public string IdLabel => $"#{ModId}/{FileId}";
    public string OptionalLabel => Optional ? "OPTIONAL" : "REQUIRED";
    public Avalonia.Media.IBrush OptionalBrush => Optional
        ? Avalonia.Media.Brushes.Gray
        : Avalonia.Media.Brushes.IndianRed;

    [ObservableProperty] private string _downloadStatus = "(not downloaded)";
    [ObservableProperty] private string _installStatus = "(not installed)";
    [ObservableProperty] private string? _downloadedPath;

    public Avalonia.Media.IBrush DownloadBrush => DownloadStatus.StartsWith("FAIL", System.StringComparison.OrdinalIgnoreCase)
        ? Avalonia.Media.Brushes.IndianRed
        : DownloadStatus == "Downloaded"
            ? Avalonia.Media.Brushes.MediumSeaGreen
            : Avalonia.Media.Brushes.Gray;

    public Avalonia.Media.IBrush InstallBrush => InstallStatus.StartsWith("FAIL", System.StringComparison.OrdinalIgnoreCase)
        ? Avalonia.Media.Brushes.IndianRed
        : InstallStatus == "Installed"
            ? Avalonia.Media.Brushes.MediumSeaGreen
            : Avalonia.Media.Brushes.Gray;

    partial void OnDownloadStatusChanged(string value) => OnPropertyChanged(nameof(DownloadBrush));
    partial void OnInstallStatusChanged(string value) => OnPropertyChanged(nameof(InstallBrush));

    public CollectionModRow(CollectionsViewModel parent, NexusCollectionModEntry e)
    {
        _parent = parent;
        ModId = e.ModId; FileId = e.FileId;
        ModName = e.ModName; FileName = e.FileName;
        Author = e.Author; Optional = e.Optional;
    }

    [RelayCommand] private async Task DownloadAsync() => await _parent.DownloadOneAsync(this);

    [RelayCommand]
    private void OpenOnNexus() => CPMM2067.App.Services.BrowserLauncher.OpenUrl(FilesPageUrl);
}
