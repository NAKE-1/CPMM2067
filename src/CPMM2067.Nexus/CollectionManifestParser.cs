using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace CPMM2067.Nexus;

/// <summary>
/// Parses a local Nexus Collections manifest file. Accepts:
///   - A raw .json file (collection_data.json shape)
///   - A .zip / .collection bundle (we hunt for the manifest JSON inside)
/// Tolerates both legacy and current schema shapes.
/// </summary>
public static class CollectionManifestParser
{
    public static NexusCollectionResult LoadFromFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        string json;
        if (ext is ".zip" or ".collection")
        {
            json = ExtractManifestFromZip(path);
        }
        else
        {
            json = File.ReadAllText(path);
        }
        return Parse(json, fallbackName: Path.GetFileNameWithoutExtension(path));
    }

    private static string ExtractManifestFromZip(string zipPath)
    {
        using var fs = File.OpenRead(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        // Look for collection_data.json, manifest.json, or any .json that has expected fields.
        var candidates = zip.Entries
            .Where(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e =>
                e.Name.Equals("collection_data.json", StringComparison.OrdinalIgnoreCase) ? 3 :
                e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ? 2 :
                e.Name.Equals("collection.json", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ToList();
        if (candidates.Count == 0)
            throw new InvalidDataException("No .json manifest found inside the zip.");

        foreach (var entry in candidates)
        {
            using var es = entry.Open();
            using var sr = new StreamReader(es);
            var content = sr.ReadToEnd();
            try
            {
                using var probe = JsonDocument.Parse(content);
                if (probe.RootElement.TryGetProperty("mods", out _) ||
                    probe.RootElement.TryGetProperty("info", out _))
                    return content;
            }
            catch
            {
                // ignore and try next candidate
            }
        }
        throw new InvalidDataException("Could not locate a collection manifest JSON inside the zip.");
    }

    public static NexusCollectionResult Parse(string json, string fallbackName)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var info = root.TryGetProperty("info", out var infoEl) ? infoEl : root;

        string name = GetString(info, "name") ?? fallbackName;
        string summary = GetString(info, "summary") ?? GetString(info, "description") ?? "";
        string author = GetString(info, "author") ?? GetString(info, "authorName") ?? "";
        int revision = info.TryGetProperty("revisionNumber", out var rv) && rv.TryGetInt32(out var rvi) ? rvi
                     : root.TryGetProperty("revisionNumber", out var rv2) && rv2.TryGetInt32(out var rvi2) ? rvi2
                     : 0;
        string slug = GetString(info, "slug") ?? GetString(root, "slug") ?? fallbackName;

        var mods = new List<NexusCollectionModEntry>();
        if (root.TryGetProperty("mods", out var modsArr) && modsArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in modsArr.EnumerateArray())
            {
                var modName = GetString(m, "name") ?? "(unnamed)";
                var optional = m.TryGetProperty("optional", out var opt) && opt.ValueKind == JsonValueKind.True;
                var domain = GetString(m, "domainName") ?? GetString(m, "gameDomain") ?? "cyberpunk2077";

                // Source shape — either nested "source" or flat
                int modId = 0, fileId = 0;
                if (m.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.Object)
                {
                    if (src.TryGetProperty("modId", out var mi)) mi.TryGetInt32(out modId);
                    if (src.TryGetProperty("fileId", out var fi)) fi.TryGetInt32(out fileId);
                }
                else
                {
                    if (m.TryGetProperty("modId", out var mi)) mi.TryGetInt32(out modId);
                    if (m.TryGetProperty("fileId", out var fi)) fi.TryGetInt32(out fileId);
                }

                if (modId == 0 && fileId == 0) continue;

                var fileName = GetString(m, "fileName") ?? GetString(m, "version") ?? $"file_{fileId}";
                var modAuthor = GetString(m, "author") ?? "";

                mods.Add(new NexusCollectionModEntry(
                    ModId: modId,
                    FileId: fileId,
                    ModName: modName,
                    FileName: fileName,
                    Author: modAuthor,
                    GameDomain: domain,
                    Optional: optional));
            }
        }

        return new NexusCollectionResult(slug, name, summary, author, revision, mods);
    }

    private static string? GetString(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v)
           && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
