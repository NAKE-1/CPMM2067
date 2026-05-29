using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CPMM2067.Backup;
using CPMM2067.Core.Backups;
using CPMM2067.Core.Game;
using CPMM2067.Core.Mods;
using Microsoft.Extensions.Logging;

namespace CPMM2067.Frameworks.RedMod;

public sealed class RedModHandler : IModFrameworkHandler
{
    public ModFramework Framework => ModFramework.RedMod;
    public bool SupportsLoadOrder => true;

    private readonly ILogger<RedModHandler> _log;
    private readonly IBackupStore _backups;
    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public RedModHandler(ILogger<RedModHandler> log, IBackupStore backups)
    {
        _log = log;
        _backups = backups;
    }

    public Task<ModFramework> DetectAsync(string extractedRootDir, CancellationToken ct = default)
    {
        if (FindRedModRoot(extractedRootDir) != null)
            return Task.FromResult(ModFramework.RedMod);
        return Task.FromResult(ModFramework.Unknown);
    }

    public async Task<ModInstallationState> InstallAsync(
        ModInstallationRequest request,
        GameInstallation game,
        CancellationToken ct = default)
    {
        var srcRoot = FindRedModRoot(request.ExtractedRootDir)
            ?? throw new InvalidOperationException("No REDmod info.json found in extracted archive");

        var folderName = SanitizeFolderName(request.SuggestedName);
        var dstRoot = Path.Combine(game.ModsDir, folderName);
        if (Directory.Exists(dstRoot))
            throw new InvalidOperationException($"REDmod folder already exists: {dstRoot}");
        Directory.CreateDirectory(dstRoot);

        var modId = ModId.NewId();
        var files = new List<InstalledFileRecord>();

        foreach (var src in Directory.EnumerateFiles(srcRoot, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(srcRoot, src);
            var dst = Path.Combine(dstRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: false);

            var gameRelPath = Path.GetRelativePath(game.InstallDir, dst);
            var sha = await FileBackupStore.HashAsync(dst, ct).ConfigureAwait(false);
            files.Add(new InstalledFileRecord
            {
                OwnerMod = modId,
                RelativePath = gameRelPath,
                Sha256 = sha,
                SizeBytes = new FileInfo(dst).Length,
                OverwroteVanilla = false,
            });
        }

        var infoJsonPath = Path.Combine(dstRoot, "info.json");
        RedModInfo? info = null;
        if (File.Exists(infoJsonPath))
        {
            try { info = JsonSerializer.Deserialize<RedModInfo>(File.ReadAllText(infoJsonPath)); }
            catch (Exception ex) { _log.LogWarning(ex, "Failed to parse info.json at {Path}", infoJsonPath); }
        }

        var manifest = new ModManifest
        {
            Id = modId,
            Name = info?.Name ?? folderName,
            Version = info?.Version ?? request.Version,
            Description = info?.Description,
            Author = request.Author,
            Framework = ModFramework.RedMod,
            Source = request.Source,
            NexusModId = request.NexusModId,
            NexusFileId = request.NexusFileId,
            NexusGameDomain = request.NexusGameDomain,
            OriginalArchivePath = request.OriginalArchivePath,
            OriginalArchiveSha256 = request.OriginalArchiveSha256,
        };

        await AppendToModsJsonAsync(game, folderName, enabled: true, ct).ConfigureAwait(false);

        _log.LogInformation("Installed REDmod {Name} v{Version} -> {Path} ({FileCount} files)",
            manifest.Name, manifest.Version, dstRoot, files.Count);

        return new ModInstallationState
        {
            Manifest = manifest,
            State = ModEnabled.Enabled,
            Files = files,
            LoadOrder = await GetLoadOrderIndexAsync(game, folderName, ct).ConfigureAwait(false),
        };
    }

    public async Task UninstallAsync(
        ModInstallationState state,
        GameInstallation game,
        CancellationToken ct = default)
    {
        var firstFile = state.Files.FirstOrDefault();
        if (firstFile == null) return;
        var modRel = firstFile.RelativePath.Split(Path.DirectorySeparatorChar);
        if (modRel.Length < 2 || !string.Equals(modRel[0], "mods", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("REDmod file layout invalid for uninstall");

        var folder = modRel[1];
        var dstRoot = Path.Combine(game.ModsDir, folder);
        if (Directory.Exists(dstRoot))
        {
            Directory.Delete(dstRoot, recursive: true);
            _log.LogInformation("Removed REDmod folder {Path}", dstRoot);
        }

        await RemoveFromModsJsonAsync(game, folder, ct).ConfigureAwait(false);
    }

    public async Task SetEnabledAsync(
        ModInstallationState state,
        ModEnabled target,
        GameInstallation game,
        CancellationToken ct = default)
    {
        var first = state.Files.FirstOrDefault();
        if (first == null) return;
        var folder = first.RelativePath.Split(Path.DirectorySeparatorChar)[1];
        await SetEnabledInModsJsonAsync(game, folder, target == ModEnabled.Enabled, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ModInstallationState>> ReorderAsync(
        IReadOnlyList<ModInstallationState> ordered,
        GameInstallation game,
        CancellationToken ct = default)
    {
        var doc = await LoadModsJsonAsync(game, ct).ConfigureAwait(false);
        var byFolder = doc.Mods.ToDictionary(m => m.Folder, m => m, StringComparer.OrdinalIgnoreCase);

        var newOrder = new List<RedModsJsonEntry>();
        foreach (var state in ordered)
        {
            var folder = state.Files.First().RelativePath.Split(Path.DirectorySeparatorChar)[1];
            if (byFolder.TryGetValue(folder, out var existing))
                newOrder.Add(existing);
            else
                newOrder.Add(new RedModsJsonEntry { Folder = folder, Enabled = state.State == ModEnabled.Enabled });
        }
        var folderSet = new HashSet<string>(newOrder.Select(m => m.Folder), StringComparer.OrdinalIgnoreCase);
        foreach (var leftover in doc.Mods)
            if (!folderSet.Contains(leftover.Folder)) newOrder.Add(leftover);

        doc.Mods.Clear();
        doc.Mods.AddRange(newOrder);
        await SaveModsJsonAsync(game, doc, ct).ConfigureAwait(false);

        return ordered.Select((s, i) => s with { LoadOrder = i }).ToList();
    }

    public async Task DeployAsync(GameInstallation game, CancellationToken ct = default)
    {
        if (!File.Exists(game.RedModExePath))
        {
            _log.LogWarning("redMod.exe not found at {Path}; skipping deploy", game.RedModExePath);
            return;
        }
        var psi = new ProcessStartInfo
        {
            FileName = game.RedModExePath,
            Arguments = "deploy",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(game.RedModExePath)!,
        };
        _log.LogInformation("Running redMod deploy: {Exe} {Args}", psi.FileName, psi.Arguments);
        using var proc = Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        if (proc.ExitCode != 0)
            _log.LogWarning("redMod deploy exit code {Code}: {Stderr}", proc.ExitCode, stderr.Trim());
        else
            _log.LogInformation("redMod deploy ok. stdout: {Out}", stdout.Trim());
    }

    private static string? FindRedModRoot(string extractedRootDir)
    {
        if (!Directory.Exists(extractedRootDir)) return null;
        var rootInfo = Path.Combine(extractedRootDir, "info.json");
        if (File.Exists(rootInfo)) return extractedRootDir;
        var modsDir = Path.Combine(extractedRootDir, "mods");
        if (Directory.Exists(modsDir))
        {
            var sub = Directory.GetDirectories(modsDir);
            if (sub.Length == 1 && File.Exists(Path.Combine(sub[0], "info.json"))) return sub[0];
        }
        foreach (var dir in Directory.EnumerateDirectories(extractedRootDir, "*", SearchOption.AllDirectories))
        {
            if (File.Exists(Path.Combine(dir, "info.json"))) return dir;
        }
        return null;
    }

    private static string SanitizeFolderName(string suggested)
    {
        var bad = Path.GetInvalidFileNameChars();
        var chars = suggested.Select(c => Array.IndexOf(bad, c) >= 0 ? '_' : c).ToArray();
        var name = new string(chars).Trim().Trim('.');
        return string.IsNullOrWhiteSpace(name) ? "unnamed_mod" : name;
    }

    private async Task<RedModsJson> LoadModsJsonAsync(GameInstallation game, CancellationToken ct)
    {
        if (!File.Exists(game.ModsJsonPath)) return new RedModsJson();
        try
        {
            await using var s = File.OpenRead(game.ModsJsonPath);
            return await JsonSerializer.DeserializeAsync<RedModsJson>(s, s_jsonOpts, ct).ConfigureAwait(false)
                   ?? new RedModsJson();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to parse mods.json; treating as empty");
            return new RedModsJson();
        }
    }

    private async Task SaveModsJsonAsync(GameInstallation game, RedModsJson doc, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(game.ModsJsonPath)!);
        await using var s = File.Create(game.ModsJsonPath);
        await JsonSerializer.SerializeAsync(s, doc, s_jsonOpts, ct).ConfigureAwait(false);
    }

    private async Task AppendToModsJsonAsync(GameInstallation game, string folder, bool enabled, CancellationToken ct)
    {
        var doc = await LoadModsJsonAsync(game, ct).ConfigureAwait(false);
        if (doc.Mods.Any(m => string.Equals(m.Folder, folder, StringComparison.OrdinalIgnoreCase))) return;
        doc.Mods.Add(new RedModsJsonEntry { Folder = folder, Enabled = enabled });
        await SaveModsJsonAsync(game, doc, ct).ConfigureAwait(false);
    }

    private async Task RemoveFromModsJsonAsync(GameInstallation game, string folder, CancellationToken ct)
    {
        var doc = await LoadModsJsonAsync(game, ct).ConfigureAwait(false);
        doc.Mods.RemoveAll(m => string.Equals(m.Folder, folder, StringComparison.OrdinalIgnoreCase));
        await SaveModsJsonAsync(game, doc, ct).ConfigureAwait(false);
    }

    private async Task SetEnabledInModsJsonAsync(GameInstallation game, string folder, bool enabled, CancellationToken ct)
    {
        var doc = await LoadModsJsonAsync(game, ct).ConfigureAwait(false);
        for (var i = 0; i < doc.Mods.Count; i++)
        {
            if (string.Equals(doc.Mods[i].Folder, folder, StringComparison.OrdinalIgnoreCase))
            {
                doc.Mods[i] = doc.Mods[i] with { Enabled = enabled };
                break;
            }
        }
        await SaveModsJsonAsync(game, doc, ct).ConfigureAwait(false);
    }

    private async Task<int> GetLoadOrderIndexAsync(GameInstallation game, string folder, CancellationToken ct)
    {
        var doc = await LoadModsJsonAsync(game, ct).ConfigureAwait(false);
        for (var i = 0; i < doc.Mods.Count; i++)
            if (string.Equals(doc.Mods[i].Folder, folder, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
