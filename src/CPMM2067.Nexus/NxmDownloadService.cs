using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CPMM2067.Nexus;

public sealed record NxmDownloadResult(bool Ok, string Message, string? LocalPath);

public delegate void NxmProgress(long bytesReceived, long? totalBytes, double percent);

public sealed class NxmDownloadService
{
    private readonly NexusApiClient _api;
    private readonly ILogger<NxmDownloadService> _log;
    private readonly HttpClient _http;

    public NxmDownloadService(NexusApiClient api, ILogger<NxmDownloadService> log)
    {
        _api = api;
        _log = log;
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CPMM2067/0.1");
    }

    public async Task<NxmDownloadResult> DownloadAsync(
        string nxmUri,
        string targetDir,
        NxmProgress? progress = null,
        CancellationToken ct = default)
    {
        (string domain, int modId, int fileId, string? key, long expires) parsed;
        try { parsed = NxmUriParser.Parse(nxmUri); }
        catch (Exception ex) { return new NxmDownloadResult(false, $"Bad nxm URI: {ex.Message}", null); }

        Directory.CreateDirectory(targetDir);

        var links = await _api.GetDownloadLinksAsync(parsed.modId, parsed.fileId, parsed.key, parsed.expires, ct)
            .ConfigureAwait(false);
        if (links == null || links.Length == 0)
        {
            return new NxmDownloadResult(false,
                "Nexus did not return a download link. Check your API key in Settings, " +
                "and note that non-premium NXM links expire shortly after the browser click.",
                null);
        }
        var url = links[0].Uri;

        var fileInfo = await _api.GetFilesAsync(parsed.modId, ct).ConfigureAwait(false);
        string fileName;
        if (fileInfo?.Files != null)
        {
            var match = fileInfo.Files.Find(f => f.FileId == parsed.fileId);
            fileName = match?.FileName ?? $"nexus_{parsed.modId}_{parsed.fileId}.zip";
        }
        else
        {
            fileName = $"nexus_{parsed.modId}_{parsed.fileId}.zip";
        }
        var localPath = Path.Combine(targetDir, fileName);

        _log.LogInformation("Downloading nxm mod={ModId} file={FileId} -> {Path}",
            parsed.modId, parsed.fileId, localPath);

        try
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength;

            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024);
            var buffer = new byte[64 * 1024];
            long received = 0;
            int read;
            var lastReport = DateTime.UtcNow.AddSeconds(-1);
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                received += read;
                if (progress != null && (DateTime.UtcNow - lastReport).TotalMilliseconds > 250)
                {
                    var pct = total.HasValue && total.Value > 0 ? (received * 100.0 / total.Value) : -1.0;
                    progress(received, total, pct);
                    lastReport = DateTime.UtcNow;
                }
            }
            progress?.Invoke(received, total ?? received, 100.0);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(localPath)) File.Delete(localPath); } catch { }
            return new NxmDownloadResult(false, $"Download failed: {ex.Message}", null);
        }

        return new NxmDownloadResult(true, $"Downloaded {fileName}", localPath);
    }
}
