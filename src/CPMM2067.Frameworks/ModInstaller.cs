using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CPMM2067.Archives;
using CPMM2067.Core;
using CPMM2067.Core.Game;
using CPMM2067.Core.Mods;
using CPMM2067.Frameworks.LegacyArchive;
using CPMM2067.Frameworks.RedMod;
using Microsoft.Extensions.Logging;

namespace CPMM2067.Frameworks;

public sealed record InstallResult(bool Ok, string Message, ModInstallationState? State = null, InstallEntry? JournalEntry = null, InstallJob? Job = null);

public sealed class ModInstaller
{
    private readonly ArchiveExtractor _extractor;
    private readonly RedModHandler _redmod;
    private readonly LegacyArchiveHandler _legacy;
    private readonly Red4ext.Red4extHandler _r4x;
    private readonly TweakXL.TweakXLHandler _tweakxl;
    private readonly Cet.CetHandler _cet;
    private readonly Redscript.RedscriptHandler _reds;
    private readonly Fomod.FomodHandler _fomod;
    private readonly Fomod.IFomodChooser? _fomodChooser;
    private readonly InstallJournal _journal;
    private readonly InstallQueue _queue;
    private readonly ILogger<ModInstaller> _log;

    public ModInstaller(
        ArchiveExtractor extractor,
        RedModHandler redmod,
        LegacyArchiveHandler legacy,
        Red4ext.Red4extHandler r4x,
        TweakXL.TweakXLHandler tweakxl,
        Cet.CetHandler cet,
        Redscript.RedscriptHandler reds,
        Fomod.FomodHandler fomod,
        InstallJournal journal,
        InstallQueue queue,
        ILogger<ModInstaller> log,
        Fomod.IFomodChooser? fomodChooser = null)
    {
        _extractor = extractor;
        _redmod = redmod;
        _legacy = legacy;
        _r4x = r4x;
        _tweakxl = tweakxl;
        _cet = cet;
        _reds = reds;
        _fomod = fomod;
        _fomodChooser = fomodChooser;
        _journal = journal;
        _queue = queue;
        _log = log;
    }

    public static string TestingRoot => Path.Combine(AppPaths.AppData, "testing");

    public async Task<InstallResult> InstallFromArchiveAsync(
        string archivePath,
        GameInstallation game,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        var job = _queue.Enqueue(archivePath);
        if (!File.Exists(archivePath))
        {
            job.Status = InstallJobStatus.Failed;
            job.StatusText = "file not found";
            return new InstallResult(false, $"File not found: {archivePath}", Job: job);
        }

        string targetExtractDir;
        if (dryRun)
        {
            Directory.CreateDirectory(TestingRoot);
            targetExtractDir = Path.Combine(TestingRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{job.Id[..8]}");
            job.TestFolder = targetExtractDir;
        }
        else
        {
            targetExtractDir = Path.Combine(Path.GetTempPath(), "cpmm2067", job.Id);
        }

        try
        {
            job.Status = InstallJobStatus.Extracting;
            job.StatusText = "extracting…";
            await _extractor.ExtractToAsync(archivePath, targetExtractDir, ct).ConfigureAwait(false);

            job.Status = InstallJobStatus.Detecting;
            job.StatusText = "detecting framework…";

            var sugg = Path.GetFileNameWithoutExtension(archivePath);

            // FOMOD short-circuit: if a ModuleConfig.xml is present, parse it and apply defaults.
            if (!dryRun && _fomod.IsFomod(targetExtractDir, out var moduleConfig))
            {
                try
                {
                    var plan = _fomod.Resolve(moduleConfig, targetExtractDir);

                    if (_fomodChooser != null)
                    {
                        var chosen = await _fomodChooser.ChooseAsync(moduleConfig, targetExtractDir).ConfigureAwait(false);
                        if (chosen == null)
                        {
                            job.Status = InstallJobStatus.Cancelled;
                            job.StatusText = "FOMOD install cancelled by user";
                            return new InstallResult(false, "Cancelled at FOMOD wizard.", Job: job);
                        }
                        if (chosen.Count > 0)
                        {
                            plan.Files.Clear();
                            plan.Files.AddRange(chosen);
                        }
                    }

                    job.Status = InstallJobStatus.Installing;
                    job.StatusText = $"FOMOD '{plan.ModuleName}' — applying {plan.Files.Count} file(s)";
                    var state = await _fomod.ApplyAsync(plan, targetExtractDir, sugg, game, ct).ConfigureAwait(false);
                    var entry = new InstallEntry
                    {
                        Name = state.Manifest.Name,
                        Version = state.Manifest.Version,
                        Framework = state.Manifest.Framework,
                        SourceArchivePath = archivePath,
                        RelativePaths = state.Files.Select(f => f.RelativePath).ToList(),
                        Status = InstallEntryStatus.Installed,
                        Notes = "FOMOD auto-defaults: " + string.Join("; ", plan.Decisions),
                        DependenciesDetected = DetectDependencies(targetExtractDir),
                    };
                    await _journal.SaveAsync(entry, ct).ConfigureAwait(false);
                    job.Status = InstallJobStatus.Done;
                    job.StatusText = "FOMOD installed";
                    job.ResultMessage = $"FOMOD '{plan.ModuleName}' applied with default options.";
                    return new InstallResult(true, job.ResultMessage, state, entry, job);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "FOMOD apply failed, falling back to per-framework detection");
                    // fall through to normal handlers
                }
            }
            var req = new ModInstallationRequest
            {
                ExtractedRootDir = targetExtractDir,
                SuggestedName = sugg,
                Version = "1.0.0",
                Source = ModSource.LocalFile,
                OriginalArchivePath = archivePath,
            };

            foreach (var handler in OrderedHandlers())
            {
                var fw = await handler.DetectAsync(targetExtractDir, ct).ConfigureAwait(false);
                if (fw == ModFramework.Unknown) continue;

                var deps = DetectDependencies(targetExtractDir);

                if (dryRun)
                {
                    var entry = new InstallEntry
                    {
                        Name = sugg,
                        Version = req.Version,
                        Framework = fw,
                        SourceArchivePath = archivePath,
                        RelativePaths = EnumerateRelative(targetExtractDir),
                        Status = InstallEntryStatus.DryRun,
                        Notes = $"Testing mode — extracted to {targetExtractDir}",
                        DependenciesDetected = deps,
                    };
                    await _journal.SaveAsync(entry, ct).ConfigureAwait(false);
                    job.Status = InstallJobStatus.DryRun;
                    job.StatusText = "dry-run done";
                    var depMsg = deps.Count == 0 ? "" : $" Deps: {string.Join(", ", deps)}.";
                    job.ResultMessage = $"[DRY RUN] {fw}: {sugg}.{depMsg} Files at: {targetExtractDir}";
                    return new InstallResult(true, job.ResultMessage, JournalEntry: entry, Job: job);
                }

                try
                {
                    job.Status = InstallJobStatus.Installing;
                    job.StatusText = $"installing as {fw}…";
                    var state = await handler.InstallAsync(req, game, ct).ConfigureAwait(false);
                    var entry = new InstallEntry
                    {
                        Name = state.Manifest.Name,
                        Version = state.Manifest.Version,
                        Framework = fw,
                        SourceArchivePath = archivePath,
                        RelativePaths = state.Files.Select(f => f.RelativePath).ToList(),
                        Status = InstallEntryStatus.Installed,
                        DependenciesDetected = deps,
                    };
                    await _journal.SaveAsync(entry, ct).ConfigureAwait(false);
                    job.Status = InstallJobStatus.Done;
                    job.StatusText = "installed";
                    job.ResultMessage = $"Installed as {fw}: {state.Manifest.Name}";
                    return new InstallResult(true, job.ResultMessage, state, entry, job);
                }
                catch (Exception ex)
                {
                    var entry = new InstallEntry
                    {
                        Name = sugg,
                        Framework = fw,
                        SourceArchivePath = archivePath,
                        Status = InstallEntryStatus.Failed,
                        Notes = ex.Message,
                    };
                    await _journal.SaveAsync(entry, ct).ConfigureAwait(false);
                    job.Status = InstallJobStatus.Failed;
                    job.StatusText = "install failed";
                    job.ResultMessage = ex.Message;
                    return new InstallResult(false, $"Install failed: {ex.Message}", JournalEntry: entry, Job: job);
                }
            }

            job.Status = InstallJobStatus.Failed;
            job.StatusText = "unsupported";
            job.ResultMessage = "Archive does not look like a supported mod.";
            return new InstallResult(false, job.ResultMessage, Job: job);
        }
        finally
        {
            if (!dryRun) TryCleanup(targetExtractDir);
        }
    }

    public async Task<bool> RevertAsync(InstallEntry entry, string journalPath, GameInstallation game, CancellationToken ct = default)
    {
        if (entry.Status != InstallEntryStatus.Installed)
        {
            await _journal.UpdateStatusAsync(journalPath, InstallEntryStatus.Reverted,
                "Was not in Installed state; marked reverted anyway.", ct).ConfigureAwait(false);
            return true;
        }

        try
        {
            foreach (var rel in entry.RelativePaths)
            {
                var abs = Path.Combine(game.InstallDir, rel);
                if (File.Exists(abs)) File.Delete(abs);
            }
            foreach (var rel in entry.RelativePaths)
            {
                var dir = Path.GetDirectoryName(Path.Combine(game.InstallDir, rel));
                while (!string.IsNullOrEmpty(dir) &&
                       dir.Length > game.InstallDir.Length &&
                       Directory.Exists(dir) &&
                       !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                    dir = Path.GetDirectoryName(dir);
                }
            }
            await _journal.UpdateStatusAsync(journalPath, InstallEntryStatus.Reverted, "Reverted by user.", ct).ConfigureAwait(false);
            _log.LogInformation("Reverted install {Name} ({Count} files)", entry.Name, entry.RelativePaths.Count);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Revert failed for {Name}", entry.Name);
            return false;
        }
    }

    private static List<string> DetectDependencies(string extractedRoot)
    {
        var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(extractedRoot)) return new();
        if (Directory.EnumerateFileSystemEntries(extractedRoot, "red4ext", SearchOption.AllDirectories).Any()) deps.Add("RED4ext");
        if (Directory.EnumerateFiles(extractedRoot, "*.xl", SearchOption.AllDirectories).Any()) deps.Add("ArchiveXL");
        if (Directory.EnumerateFiles(extractedRoot, "*.yaml", SearchOption.AllDirectories).Any(p => p.Contains("tweaks"))) deps.Add("TweakXL");
        if (Directory.EnumerateFiles(extractedRoot, "*.reds", SearchOption.AllDirectories).Any()) deps.Add("redscript");
        if (Directory.EnumerateFileSystemEntries(extractedRoot, "cyber_engine_tweaks", SearchOption.AllDirectories).Any()) deps.Add("Cyber Engine Tweaks");
        if (Directory.EnumerateFileSystemEntries(extractedRoot, "Codeware", SearchOption.AllDirectories).Any()) deps.Add("Codeware");
        return deps.OrderBy(x => x).ToList();
    }

    private static List<string> EnumerateRelative(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p))
            .ToList();
    }

    private IEnumerable<IModFrameworkHandler> OrderedHandlers()
    {
        yield return _redmod;   // info.json manifest
        yield return _cet;      // bin/x64/plugins/cyber_engine_tweaks/mods/<name>/  (more specific than RED4ext)
        yield return _r4x;      // red4ext/, bin/x64/*.dll loader payloads (also captures r6/* alongside)
        yield return _tweakxl;  // r6/tweaks/*.yaml
        yield return _reds;     // standalone r6/scripts/*.reds (only when no RED4ext payload)
        yield return _legacy;   // archive/pc/mod/*.archive (last resort)
    }

    private static void TryCleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { }
    }
}
