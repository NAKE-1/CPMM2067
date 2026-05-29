using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CPMM2067.Core.Game;

namespace CPMM2067.Diagnostics;

public sealed record GameLogFile(string Source, string DisplayName, string AbsolutePath, long SizeBytes, DateTime LastWriteUtc);

public sealed class GameLogReader
{
    public IReadOnlyList<GameLogFile> Enumerate(GameInstallation game)
    {
        var results = new List<GameLogFile>();
        foreach (var (source, dir, patterns) in KnownLogRoots(game))
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var pattern in patterns)
            {
                foreach (var f in Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories))
                {
                    var info = new FileInfo(f);
                    results.Add(new GameLogFile(
                        Source: source,
                        DisplayName: Path.GetRelativePath(game.InstallDir, f).Replace('\\', '/'),
                        AbsolutePath: f,
                        SizeBytes: info.Length,
                        LastWriteUtc: info.LastWriteTimeUtc));
                }
            }
        }
        return results.OrderByDescending(r => r.LastWriteUtc).ToList();
    }

    public async Task<string> ReadTailAsync(string absolutePath, int maxBytes = 256_000, CancellationToken ct = default)
    {
        if (!File.Exists(absolutePath)) return $"(file missing: {absolutePath})";
        await using var fs = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length > maxBytes)
            fs.Seek(-maxBytes, SeekOrigin.End);
        using var sr = new StreamReader(fs);
        var content = await sr.ReadToEndAsync(ct).ConfigureAwait(false);
        return fs.Length > maxBytes ? "[…truncated, showing tail…]\n" + content : content;
    }

    private static IEnumerable<(string Source, string Dir, string[] Patterns)> KnownLogRoots(GameInstallation game)
    {
        yield return ("red4ext",  Path.Combine(game.InstallDir, "red4ext", "logs"),                                 new[] { "*.log", "*.txt" });
        yield return ("r6",       Path.Combine(game.InstallDir, "r6", "logs"),                                      new[] { "*.log", "*.txt" });
        yield return ("r6cache",  Path.Combine(game.InstallDir, "r6", "cache", "modded"),                           new[] { "*.log", "*.txt" });
        yield return ("redscript",Path.Combine(game.InstallDir, "r6", "cache"),                                     new[] { "redscript_rCURRENT.log", "redscript_*.log" });
        yield return ("cet",      Path.Combine(game.InstallDir, "bin", "x64", "plugins", "cyber_engine_tweaks"),    new[] { "*.log" });
        yield return ("cet-mods", Path.Combine(game.InstallDir, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods"), new[] { "*.log" });
        yield return ("redmod",   Path.Combine(game.InstallDir, "tools", "redmod"),                                 new[] { "*.log", "*.txt" });
        yield return ("crash",    Path.Combine(game.InstallDir, "bin", "x64"),                                      new[] { "*.dmp" });
    }
}
