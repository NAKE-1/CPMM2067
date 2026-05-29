using System;
using System.Collections.Generic;
using System.IO;

namespace CPMM2067.App.Services;

public static class EnvFile
{
    public static IReadOnlyDictionary<string, string> LoadFromNearestDotEnv()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in CandidatePaths())
        {
            if (!File.Exists(path)) continue;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim().Trim('"');
                if (key.Length > 0) dict[key] = val;
            }
            break;
        }
        return dict;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        yield return Path.Combine(Environment.CurrentDirectory, ".env");
        yield return Path.Combine(AppContext.BaseDirectory, ".env");
        // walk up from BaseDirectory to find a repo-root .env (handy in dev)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir != null; i++)
        {
            yield return Path.Combine(dir.FullName, ".env");
            dir = dir.Parent;
        }
    }
}
