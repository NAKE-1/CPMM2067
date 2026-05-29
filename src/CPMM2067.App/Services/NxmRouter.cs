using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CPMM2067.Core;
using CPMM2067.App.ViewModels;
using CPMM2067.Frameworks;
using CPMM2067.Nexus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CPMM2067.App.Services;

public sealed class NxmRouter
{
    private readonly NxmDownloadService _downloader;
    private readonly NexusApiClient _api;
    private readonly InstallQueue _queue;
    private readonly InstallJournal _journal;
    private readonly ModInstaller _installer;
    private readonly GameStateService _state;
    private readonly ILogger<NxmRouter> _log;

    public NxmRouter(
        NxmDownloadService downloader,
        NexusApiClient api,
        InstallQueue queue,
        InstallJournal journal,
        ModInstaller installer,
        GameStateService state,
        ILogger<NxmRouter> log)
    {
        _downloader = downloader;
        _api = api;
        _queue = queue;
        _journal = journal;
        _installer = installer;
        _state = state;
        _log = log;
    }

    public async Task HandleAsync(string nxmUri)
    {
        _log.LogInformation("NXM received: {Uri}", nxmUri);

        // Branch by URL shape — collections vs mod files vs garbage
        var kind = NxmUriParser.Classify(nxmUri);
        if (kind == NxmKind.Collection)
        {
            HandleCollection(nxmUri);
            return;
        }
        if (kind != NxmKind.ModFile)
        {
            EnqueueFailed(nxmUri, $"Unrecognised nxm URL shape: {nxmUri}");
            return;
        }

        var key = AppHost.Settings.NexusApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            EnqueueFailed(nxmUri, "No Nexus API key — set one in Settings first.");
            return;
        }
        _api.SetApiKey(key.Trim());

        var job = _queue.Enqueue(nxmUri, displayName: "(resolving…)");
        job.Status = InstallJobStatus.Queued;
        job.StatusText = "looking up mod info…";

        // Resolve real names from the Nexus API
        try
        {
            var parsed = NxmUriParser.Parse(nxmUri);
            var modInfo = await _api.GetModAsync(parsed.ModId, job.Cts.Token).ConfigureAwait(false);
            var files = await _api.GetFilesAsync(parsed.ModId, job.Cts.Token).ConfigureAwait(false);
            var fileEntry = files?.Files?.Find(f => f.FileId == parsed.FileId);
            if (modInfo != null)
            {
                var fileName = fileEntry?.Name ?? fileEntry?.FileName ?? "";
                Dispatcher.UIThread.Post(() =>
                {
                    job.Name = string.IsNullOrEmpty(fileName)
                        ? modInfo.Name
                        : $"{modInfo.Name} — {fileName}";
                });
            }
        }
        catch (OperationCanceledException) { job.Cancel(); return; }
        catch (Exception ex) { _log.LogDebug(ex, "Could not resolve mod name; falling back"); }

        if (job.Cts.IsCancellationRequested) { job.Cancel(); return; }

        job.Status = InstallJobStatus.Downloading;
        job.StatusText = "starting download…";

        AppPaths.EnsureAll();
        var cacheDir = AppPaths.ArchiveCacheDir;
        Directory.CreateDirectory(cacheDir);

        var started = DateTime.UtcNow;
        long lastBytes = 0;
        var lastTick = DateTime.UtcNow;

        NxmDownloadResult dl;
        try
        {
            dl = await _downloader.DownloadAsync(nxmUri, cacheDir,
                progress: (received, total, pct) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        var now = DateTime.UtcNow;
                        var dt = (now - lastTick).TotalSeconds;
                        var speed = dt > 0 ? (received - lastBytes) / dt / 1024.0 / 1024.0 : 0.0;
                        lastTick = now;
                        lastBytes = received;

                        job.BytesReceived = received;
                        job.BytesTotal = total ?? 0;
                        job.ProgressPercent = pct < 0 ? -1 : (int)pct;
                        job.SpeedMBps = speed;
                        if (total.HasValue && speed > 0)
                        {
                            var remainingBytes = total.Value - received;
                            job.EtaRemaining = TimeSpan.FromSeconds(remainingBytes / (speed * 1024.0 * 1024.0));
                        }

                        var mb = received / 1024.0 / 1024.0;
                        var totMb = total.HasValue ? total.Value / 1024.0 / 1024.0 : -1;
                        var etaStr = job.EtaRemaining.HasValue ? $" ETA {FormatEta(job.EtaRemaining.Value)}" : "";
                        job.StatusText = totMb > 0
                            ? $"downloading… {mb:F1} / {totMb:F1} MB @ {speed:F1} MB/s ({(int)pct}%){etaStr}"
                            : $"downloading… {mb:F1} MB @ {speed:F1} MB/s";
                    });
                }, ct: job.Cts.Token);
        }
        catch (OperationCanceledException) { job.Cancel(); return; }

        if (job.Cts.IsCancellationRequested) { job.Cancel(); return; }

        if (!dl.Ok || dl.LocalPath == null)
        {
            EnqueueFailed(nxmUri, dl.Message, existingJob: job);
            return;
        }

        var size = new FileInfo(dl.LocalPath).Length;
        var elapsed = DateTime.UtcNow - started;
        _log.LogInformation("Downloaded {Path} ({Mb:F1} MB in {Secs:F1}s)", dl.LocalPath, size / 1024.0 / 1024.0, elapsed.TotalSeconds);

        var downloadedEntry = new InstallEntry
        {
            Name = job.Name,
            Framework = Core.Mods.ModFramework.Unknown,
            SourceArchivePath = dl.LocalPath,
            Status = InstallEntryStatus.Downloaded,
            Notes = $"From {nxmUri} — {size / 1024.0 / 1024.0:F1} MB in {elapsed.TotalSeconds:F1}s",
            RelativePaths = new List<string>(),
            DependenciesDetected = new List<string>(),
        };
        await _journal.SaveAsync(downloadedEntry);
        job.Status = InstallJobStatus.Done;
        job.StatusText = "downloaded — click [ INSTALL ] in Downloads to apply";
        job.ResultMessage = $"Cached at {dl.LocalPath}";

        DownloadCompleted?.Invoke(downloadedEntry);
    }

    public event Action<InstallEntry>? DownloadCompleted;

    private void HandleCollection(string nxmUri)
    {
        if (!NxmUriParser.TryParseCollection(nxmUri, out var col) || col == null)
        {
            EnqueueFailed(nxmUri, "Could not parse collection nxm URL.");
            return;
        }

        var job = _queue.Enqueue(nxmUri, displayName: $"Collection {col.Slug} rev {col.Revision}");
        job.Status = InstallJobStatus.Done;     // we don't auto-download collection bundles yet
        job.StatusText = "collection URL routed — open Collections page";
        job.ResultMessage =
            $"Received nxm collection URL for {col.Domain} / {col.Slug} / rev {col.Revision}.\n" +
            "Auto-download of collection bundles isn't implemented yet (Nexus requires JWT + GraphQL " +
            "for that). On the Nexus page, click [ Download Collection ] to save the .collection bundle, " +
            "then open the Collections sidebar → [ LOAD COLLECTION FILE ] in CPMM2067.";

        // Pre-fill the Collections page slug input so user just has to click LOAD COLLECTION FILE
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var vm = AppHost.Services.GetService(typeof(ViewModels.CollectionsViewModel)) as ViewModels.CollectionsViewModel;
                if (vm != null) vm.SlugInput = col.Slug;
            }
            catch { /* best effort */ }
        });

        _log.LogInformation("Collection nxm routed: slug={Slug} rev={Rev}", col.Slug, col.Revision);
    }

    private void EnqueueFailed(string uri, string msg, InstallJob? existingJob = null)
    {
        var job = existingJob ?? _queue.Enqueue(uri, displayName: TruncateUri(uri));
        job.Status = InstallJobStatus.Failed;
        job.StatusText = "failed";
        job.ResultMessage = msg;
        _log.LogWarning("NXM failed: {Msg}", msg);
    }

    private static string TruncateUri(string uri) => uri.Length > 60 ? uri[..57] + "…" : uri;

    private static string FormatEta(TimeSpan ts) =>
        ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
                           : $"{ts.Minutes}:{ts.Seconds:00}";
}
