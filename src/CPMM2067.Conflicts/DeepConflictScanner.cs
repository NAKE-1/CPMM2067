using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CPMM2067.Core.Game;

namespace CPMM2067.Conflicts;

public sealed record DeepConflict(string Key, string Kind, IReadOnlyList<string> SourceFiles);

public sealed class DeepConflictReport
{
    public List<DeepConflict> TweakXLKeyConflicts { get; } = new();
    public List<DeepConflict> RedscriptHookConflicts { get; } = new();
}

public static class DeepConflictScanner
{
    private static readonly Regex YamlTopKey =
        new(@"^([A-Za-z][\w.\-]*)\s*:", RegexOptions.Compiled);

    // Match annotations like @addMethod(PlayerPuppet) on one line + a following func declaration
    // anywhere in the next ~5 non-blank lines.
    private static readonly Regex RedsAnnotation =
        new(@"@(?<ann>addMethod|replaceMethod|wrapMethod|addField|replaceGlobal)\s*\(\s*(?<cls>\w+)",
            RegexOptions.Compiled);
    private static readonly Regex RedsFuncDecl =
        new(@"\bfunc\s+(?<name>\w+)\s*\(", RegexOptions.Compiled);

    public static DeepConflictReport Scan(GameInstallation game)
    {
        var report = new DeepConflictReport();
        ScanTweakXL(game, report);
        ScanRedscript(game, report);
        return report;
    }

    private static void ScanTweakXL(GameInstallation game, DeepConflictReport report)
    {
        if (!Directory.Exists(game.R6TweaksDir)) return;
        var keyToFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(game.R6TweaksDir, "*.yaml", SearchOption.AllDirectories))
        {
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch { continue; }
            foreach (var raw in lines)
            {
                if (raw.Length == 0 || char.IsWhiteSpace(raw[0])) continue;
                if (raw.TrimStart().StartsWith("#")) continue;
                var m = YamlTopKey.Match(raw);
                if (!m.Success) continue;
                var key = m.Groups[1].Value;
                if (!keyToFiles.TryGetValue(key, out var list))
                    keyToFiles[key] = list = new();
                var rel = Path.GetRelativePath(game.InstallDir, path).Replace('\\', '/');
                if (!list.Contains(rel)) list.Add(rel);
            }
        }
        foreach (var (key, files) in keyToFiles)
            if (files.Count > 1)
                report.TweakXLKeyConflicts.Add(new DeepConflict(key, "TweakXL key", files));
        report.TweakXLKeyConflicts.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
    }

    private static void ScanRedscript(GameInstallation game, DeepConflictReport report)
    {
        if (!Directory.Exists(game.R6ScriptsDir)) return;
        var hookToFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(game.R6ScriptsDir, "*.reds", SearchOption.AllDirectories))
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch { continue; }
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var a = RedsAnnotation.Match(lines[i]);
                if (!a.Success) continue;
                var ann = a.Groups["ann"].Value;
                var cls = a.Groups["cls"].Value;

                // Look ahead up to 4 lines for func declaration
                for (var j = i; j < Math.Min(lines.Length, i + 5); j++)
                {
                    var f = RedsFuncDecl.Match(lines[j]);
                    if (!f.Success) continue;
                    var fn = f.Groups["name"].Value;
                    var key = $"{ann} {cls}.{fn}";
                    if (!hookToFiles.TryGetValue(key, out var list))
                        hookToFiles[key] = list = new();
                    var rel = Path.GetRelativePath(game.InstallDir, path).Replace('\\', '/');
                    if (!list.Contains(rel)) list.Add(rel);
                    break;
                }
            }
        }
        foreach (var (key, files) in hookToFiles)
            if (files.Count > 1)
                report.RedscriptHookConflicts.Add(new DeepConflict(key, "redscript hook", files));
        report.RedscriptHookConflicts.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
    }
}
