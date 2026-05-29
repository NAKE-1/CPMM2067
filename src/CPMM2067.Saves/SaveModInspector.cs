using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CPMM2067.Saves;

/// <summary>
/// Heuristic mod-fingerprint scan of a Cyberpunk 2077 save folder.
/// Does NOT parse the CR2W save format. Instead:
///   1. Reads metadata.9.json for the game version + counts + timestamps.
///   2. Scans sav.dat byte-by-byte for runs of printable ASCII (with attempted
///      zlib decompression where present) and extracts TweakDB-style identifiers
///      ("Foo.Bar.Baz").
///   3. Buckets identifiers by prefix into "vanilla-looking" vs "mod-likely".
/// </summary>
public static class SaveModInspector
{
    // TweakDB prefixes the vanilla game uses. Anything outside these (and matching
    // an ID-shaped pattern) is flagged as potentially mod-added.
    private static readonly HashSet<string> VanillaPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Items", "Item", "Weapon", "Weapons", "Vehicle", "Vehicles",
        "Quest", "Quests", "Character", "Characters", "NPC", "NPCs",
        "Attachments", "Attachment", "Hairstyles", "Hairstyle",
        "Eyebrows", "Beards", "Makeups", "Tattoos", "Piercings", "Cyberware",
        "Outfit", "Outfits", "Clothing", "Player", "AI", "Stat", "Stats",
        "Perks", "Perk", "Attributes", "Attribute", "Effects", "Effect",
        "Animations", "Animation", "Locomotion", "Damage", "RPG", "Crafting",
        "Records", "BaseStats", "Inputs", "Ammo", "Currency", "Quality",
        "ItemsFactory", "VehicleFactory", "EquipmentArea", "ArchetypeID",
        "Audio", "Sound", "Voiceover", "VFX", "Texture", "GameplayLogicPackages",
        "GenericNotificationViewData", "UIIcon", "WorldMapItems", "Programs",
        "Garage", "Tutorial", "Tutorials", "Streets", "Districts",
    };

    public static SaveScanResult Inspect(string saveDir)
    {
        var result = new SaveScanResult { SaveDir = saveDir };

        // metadata.json
        var meta = Directory.EnumerateFiles(saveDir, "metadata*.json").FirstOrDefault();
        if (meta != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(meta));
                result.MetadataJson = doc.RootElement.GetRawText();
                if (doc.RootElement.TryGetProperty("gameVersion", out var gv))
                    result.GameVersion = gv.GetRawText().Trim('"');
                if (doc.RootElement.TryGetProperty("name", out var nm))
                    result.SaveName = nm.GetString();
                if (doc.RootElement.TryGetProperty("playthroughID", out var pid))
                    result.PlaythroughId = pid.GetRawText().Trim('"');
            }
            catch { /* leave fields blank */ }
        }

        // sav.dat scan
        var savDat = Path.Combine(saveDir, "sav.dat");
        if (File.Exists(savDat))
        {
            result.SavDatBytes = new FileInfo(savDat).Length;
            var bytes = ReadAllBytesCapped(savDat, max: 256 * 1024 * 1024);
            var strings = ScanForStrings(bytes);

            // Try decompressing chunks of the file too. CP2077 saves store
            // CR2W chunks compressed with zlib/raw deflate.
            foreach (var sub in TryDeflateChunks(bytes, max: 32))
                foreach (var s in ScanForStrings(sub)) strings.Add(s);

            var ids = strings
                .Where(IsTweakDbIdShaped)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var id in ids)
            {
                var prefix = id.Split('.')[0];
                if (VanillaPrefixes.Contains(prefix)) result.VanillaIds.Add(id);
                else result.ModLikelyIds.Add(id);
            }

            // Also surface "interesting" non-ID strings that smell mod-y.
            foreach (var s in strings.Where(LooksLikeModName).Distinct())
                result.ModNameHints.Add(s);
        }

        result.VanillaIds.Sort(StringComparer.OrdinalIgnoreCase);
        result.ModLikelyIds.Sort(StringComparer.OrdinalIgnoreCase);
        result.ModNameHints.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static byte[] ReadAllBytesCapped(string path, long max)
    {
        var info = new FileInfo(path);
        if (info.Length <= max) return File.ReadAllBytes(path);
        var buf = new byte[max];
        using var fs = File.OpenRead(path);
        fs.Read(buf, 0, buf.Length);
        return buf;
    }

    private static HashSet<string> ScanForStrings(byte[] bytes)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var b in bytes)
        {
            if (b >= 0x20 && b < 0x7F)
            {
                sb.Append((char)b);
            }
            else
            {
                if (sb.Length >= 6) set.Add(sb.ToString());
                sb.Clear();
            }
        }
        if (sb.Length >= 6) set.Add(sb.ToString());
        return set;
    }

    private static IEnumerable<byte[]> TryDeflateChunks(byte[] bytes, int max)
    {
        // Scan for zlib magic (0x78 0x9C, 0x78 0xDA, 0x78 0x01) and attempt
        // to decompress a window of bytes. Best-effort; ignore failures.
        var count = 0;
        for (var i = 0; i + 2 < bytes.Length && count < max; i++)
        {
            if (bytes[i] != 0x78) continue;
            if (bytes[i + 1] != 0x9C && bytes[i + 1] != 0xDA && bytes[i + 1] != 0x01) continue;
            byte[]? data = null;
            try
            {
                using var src = new MemoryStream(bytes, i, Math.Min(8 * 1024 * 1024, bytes.Length - i));
                using var zs = new ZLibStream(src, CompressionMode.Decompress);
                using var dst = new MemoryStream();
                zs.CopyTo(dst);
                data = dst.ToArray();
            }
            catch { /* not a real stream, skip */ }
            if (data != null && data.Length > 0)
            {
                count++;
                yield return data;
                i += 1024; // skip ahead to avoid re-scanning the same stream
            }
        }
    }

    private static bool IsTweakDbIdShaped(string s)
    {
        if (s.Length < 8 || s.Length > 120) return false;
        if (s.IndexOf('.') <= 0) return false;
        // Must look like Word.Word(.Word)* — letters, digits, underscore
        foreach (var c in s)
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.')) return false;
        // Must start with a letter
        if (!char.IsLetter(s[0])) return false;
        return true;
    }

    private static bool LooksLikeModName(string s)
    {
        if (s.Length < 6 || s.Length > 60) return false;
        var lower = s.ToLowerInvariant();
        if (lower.Contains("redmod") || lower.Contains("archivexl") || lower.Contains("tweakxl")
            || lower.Contains("cybercmd") || lower.Contains("codeware") || lower.Contains("red4ext")
            || lower.Contains("cyberengine") || lower.Contains("modded") || lower.Contains("nexus"))
        {
            return true;
        }
        return false;
    }
}

public sealed class SaveScanResult
{
    public string SaveDir { get; init; } = string.Empty;
    public string? SaveName { get; set; }
    public string? GameVersion { get; set; }
    public string? PlaythroughId { get; set; }
    public string? MetadataJson { get; set; }
    public long SavDatBytes { get; set; }
    public List<string> VanillaIds { get; } = new();
    public List<string> ModLikelyIds { get; } = new();
    public List<string> ModNameHints { get; } = new();

    public string ToReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"save dir       : {SaveDir}");
        sb.AppendLine($"name           : {SaveName ?? "—"}");
        sb.AppendLine($"gameVersion    : {GameVersion ?? "—"}");
        sb.AppendLine($"playthroughId  : {PlaythroughId ?? "—"}");
        sb.AppendLine($"sav.dat size   : {SavDatBytes / 1024.0 / 1024.0:F2} MB");
        sb.AppendLine();
        sb.AppendLine($"[ mod-likely IDs found : {ModLikelyIds.Count} ]");
        if (ModLikelyIds.Count == 0) sb.AppendLine("  (none — save looks vanilla or save is fully compressed)");
        else foreach (var s in ModLikelyIds.Take(80)) sb.AppendLine("  • " + s);
        if (ModLikelyIds.Count > 80) sb.AppendLine($"  …and {ModLikelyIds.Count - 80} more");
        sb.AppendLine();
        sb.AppendLine($"[ mod-name hints : {ModNameHints.Count} ]");
        if (ModNameHints.Count == 0) sb.AppendLine("  (none)");
        else foreach (var s in ModNameHints.Take(40)) sb.AppendLine("  • " + s);
        sb.AppendLine();
        sb.AppendLine($"[ vanilla-shaped IDs (for context) : {VanillaIds.Count} ]");
        foreach (var s in VanillaIds.Take(20)) sb.AppendLine("  • " + s);
        if (VanillaIds.Count > 20) sb.AppendLine($"  …and {VanillaIds.Count - 20} more");
        sb.AppendLine();
        sb.AppendLine("note: heuristic. False positives possible (e.g. modded TweakDB record names that share vanilla prefixes won't be flagged). False negatives are normal too if the save's content is fully zlib-encoded and not detected as a stream. For a precise scan, wait for v1.4 with CyberCAT integration.");
        return sb.ToString();
    }
}
