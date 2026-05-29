using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using CPMM2067.Core.Game;

namespace CPMM2067.Diagnostics;

public enum LoadStatus { Loaded, Failed, Unknown }

public enum LoaderSource { Cet, Red4ext, Redscript, RedMod, Codeware }

public sealed record LoadedMod(
    string Name,
    string? Version,
    LoaderSource Source,
    LoadStatus Status,
    string? Error = null);

public static class LoadedModParser
{
    // ---------------------------------------------------------------
    // CET log:   bin/x64/plugins/cyber_engine_tweaks/cyber_engine_tweaks.log
    // Lines look like:
    //   [info] Loading mod 'AppearanceMenuMod'
    //   [info] Mod 'AppearanceMenuMod' (v1.2) loaded
    //   [error] Failed to load mod 'foo': <reason>
    // ---------------------------------------------------------------
    private static readonly Regex CetLoaded =
        new(@"Mod\s+['""]?(?<n>[^'""]+)['""]?\s*(\(v?(?<v>[^)]+)\))?\s*loaded",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CetFailed =
        new(@"Failed to load (?:mod\s+)?['""]?(?<n>[^'"":]+)['""]?\s*:?\s*(?<err>.*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ---------------------------------------------------------------
    // RED4ext log: red4ext/logs/red4ext.log
    //   [info] [SDK] Loaded plugin 'TweakXL' (1.10.0)
    //   [info] [SDK] Plugin 'ArchiveXL' (1.16.0) loaded
    //   [error] [SDK] Failed to load 'foo.dll'
    // ---------------------------------------------------------------
    private static readonly Regex Red4extLoaded =
        new(@"(?:Loaded|Plugin)\s+(?:plugin\s+)?['""]?(?<n>[^'""()]+?)['""]?\s*(?:\((?<v>[^)]+)\))?\s*(?:loaded)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Red4extFailed =
        new(@"Failed to load\s+['""]?(?<n>[^'""]+)['""]?(?:\s*:\s*(?<err>.*))?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ---------------------------------------------------------------
    // redscript log: r6/cache/redscript_rCURRENT.log
    //   Compiling 47 redscript files
    //   Loaded bundle 'Codeware' v1.13.0
    //   Syntax error in r6\scripts\Mod\foo.reds:42
    // ---------------------------------------------------------------
    private static readonly Regex RedsLoadedBundle =
        new(@"(?:Loaded|Compiled)\s+(?:bundle|module)?\s*['""]?(?<n>[^'""]+)['""]?(?:\s+v?(?<v>[\d.]+))?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RedsError =
        new(@"(?:Syntax error|error).*?[\\/](?<n>[^\\/]+?)[\\/][^\\/]+\.reds(?::(?<line>\d+))?\s*(?<err>.*)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ---------------------------------------------------------------

    public static IReadOnlyList<LoadedMod> ParseAll(GameInstallation game)
    {
        var result = new List<LoadedMod>();
        try { result.AddRange(ParseCet(game)); } catch { }
        try { result.AddRange(ParseRed4ext(game)); } catch { }
        try { result.AddRange(ParseRedscript(game)); } catch { }
        try { result.AddRange(ParseRedModMetadata(game)); } catch { }
        return Dedupe(result);
    }

    public static IEnumerable<LoadedMod> ParseCet(GameInstallation game)
    {
        var path = Path.Combine(game.InstallDir,
            "bin", "x64", "plugins", "cyber_engine_tweaks", "cyber_engine_tweaks.log");
        if (!File.Exists(path)) yield break;

        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in ReadLinesCapped(path, 200_000))
        {
            var l = CetLoaded.Match(line);
            if (l.Success) loaded.Add(l.Groups["n"].Value.Trim());
            var f = CetFailed.Match(line);
            if (f.Success)
            {
                var name = f.Groups["n"].Value.Trim();
                if (!string.IsNullOrEmpty(name)) failed[name] = f.Groups["err"].Value.Trim();
            }
        }

        foreach (var n in loaded)
            yield return new LoadedMod(n, null, LoaderSource.Cet, LoadStatus.Loaded);
        foreach (var kv in failed)
            if (!loaded.Contains(kv.Key))
                yield return new LoadedMod(kv.Key, null, LoaderSource.Cet, LoadStatus.Failed, kv.Value);
    }

    public static IEnumerable<LoadedMod> ParseRed4ext(GameInstallation game)
    {
        var dir = Path.Combine(game.InstallDir, "red4ext", "logs");
        if (!Directory.Exists(dir)) yield break;
        var path = Directory.EnumerateFiles(dir, "*.log")
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
        if (path == null) yield break;

        foreach (var line in ReadLinesCapped(path, 200_000))
        {
            if (!line.Contains("[SDK]", StringComparison.OrdinalIgnoreCase)) continue;

            var l = Red4extLoaded.Match(line);
            if (l.Success && line.Contains("Loaded", StringComparison.OrdinalIgnoreCase))
            {
                var n = l.Groups["n"].Value.Replace("plugin", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (string.IsNullOrEmpty(n)) continue;
                yield return new LoadedMod(n, l.Groups["v"].Value.Trim(' ', '(', ')'),
                    LoaderSource.Red4ext, LoadStatus.Loaded);
                continue;
            }
            var f = Red4extFailed.Match(line);
            if (f.Success)
                yield return new LoadedMod(f.Groups["n"].Value, null,
                    LoaderSource.Red4ext, LoadStatus.Failed, f.Groups["err"].Value);
        }
    }

    public static IEnumerable<LoadedMod> ParseRedscript(GameInstallation game)
    {
        var path = Path.Combine(game.InstallDir, "r6", "cache", "redscript_rCURRENT.log");
        if (!File.Exists(path))
        {
            // fallback: any redscript_*.log
            var dir = Path.Combine(game.InstallDir, "r6", "cache");
            if (Directory.Exists(dir))
                path = Directory.EnumerateFiles(dir, "redscript_*.log")
                    .OrderByDescending(File.GetLastWriteTime)
                    .FirstOrDefault() ?? path;
        }
        if (!File.Exists(path)) yield break;

        foreach (var line in ReadLinesCapped(path, 200_000))
        {
            var l = RedsLoadedBundle.Match(line);
            if (l.Success && (line.Contains("Loaded", StringComparison.OrdinalIgnoreCase)
                              || line.Contains("Compiled", StringComparison.OrdinalIgnoreCase)))
            {
                yield return new LoadedMod(l.Groups["n"].Value.Trim(),
                    l.Groups["v"].Value, LoaderSource.Redscript, LoadStatus.Loaded);
                continue;
            }
            var e = RedsError.Match(line);
            if (e.Success)
                yield return new LoadedMod(e.Groups["n"].Value, null,
                    LoaderSource.Redscript, LoadStatus.Failed, line);
        }
    }

    public static IEnumerable<LoadedMod> ParseRedModMetadata(GameInstallation game)
    {
        var path = Path.Combine(game.InstallDir, "tools", "redmod", "metadata.json");
        if (!File.Exists(path)) yield break;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllText(path)); } catch { yield break; }
        using (doc)
        {
            // metadata.json shape varies by REDmod version. Common patterns:
            //   { "mods": [ { "name": "...", "version": "...", "deployed": true } ] }
            //   { "deployedMods": [ "modA", "modB" ] }
            if (doc.RootElement.TryGetProperty("mods", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in arr.EnumerateArray())
                {
                    var name = m.TryGetProperty("name", out var nm) ? nm.GetString()
                              : m.TryGetProperty("folder", out var fl) ? fl.GetString() : null;
                    if (string.IsNullOrEmpty(name)) continue;
                    var version = m.TryGetProperty("version", out var v) ? v.GetString() : null;
                    yield return new LoadedMod(name, version, LoaderSource.RedMod, LoadStatus.Loaded);
                }
            }
            else if (doc.RootElement.TryGetProperty("deployedMods", out var arr2)
                     && arr2.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in arr2.EnumerateArray())
                {
                    var name = m.GetString();
                    if (!string.IsNullOrEmpty(name))
                        yield return new LoadedMod(name, null, LoaderSource.RedMod, LoadStatus.Loaded);
                }
            }
        }
    }

    private static IEnumerable<string> ReadLinesCapped(string path, int maxLines)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                       FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        var count = 0;
        string? line;
        while ((line = sr.ReadLine()) != null && count < maxLines)
        {
            count++;
            yield return line;
        }
    }

    private static IReadOnlyList<LoadedMod> Dedupe(List<LoadedMod> entries)
    {
        return entries
            .GroupBy(e => (e.Source, e.Name), comparer: null)
            .Select(g => g.OrderByDescending(e => e.Status == LoadStatus.Loaded ? 1 : 0).First())
            .OrderBy(e => e.Source).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
