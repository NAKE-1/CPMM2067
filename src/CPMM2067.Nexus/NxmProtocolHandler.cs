using System;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace CPMM2067.Nexus;

[SupportedOSPlatform("windows")]
public sealed class NxmProtocolHandler
{
    private readonly ILogger<NxmProtocolHandler> _log;

    public NxmProtocolHandler(ILogger<NxmProtocolHandler> log) => _log = log;

    public void Register(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Executable path is empty", nameof(executablePath));
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Cannot register nxm handler — exe not found", executablePath);

        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\nxm")!)
        {
            key.SetValue(string.Empty, "URL:NXM Protocol");
            key.SetValue("URL Protocol", string.Empty);

            using var defaultIcon = key.CreateSubKey("DefaultIcon")!;
            defaultIcon.SetValue(string.Empty, $"\"{executablePath}\",0");

            using var shellOpenCommand = key.CreateSubKey(@"shell\open\command")!;
            shellOpenCommand.SetValue(string.Empty, $"\"{executablePath}\" --nxm \"%1\"");
        }

        // UserChoice (per-user default) — best-effort, may exist already
        try
        {
            using var uc = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\nxm\UserChoice", writable: true);
            // We can't set ProgId here directly — Windows protects UserChoice writes since 1803.
            // Leaving HKCU\Software\Classes\nxm is enough; Windows will pick it up when no UserChoice is set.
        }
        catch { /* ignored */ }

        _log.LogInformation("Registered nxm:// handler -> {Exe}", executablePath);
    }

    public void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\nxm", throwOnMissingSubKey: false);
            _log.LogInformation("Unregistered nxm:// handler");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to unregister nxm:// handler");
        }
    }

    public bool IsRegisteredForUs(string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\nxm\shell\open\command");
            var cmd = key?.GetValue(string.Empty) as string;
            return cmd != null && cmd.Contains(executablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

}

public enum NxmKind { ModFile, Collection, Unknown }

public sealed record NxmModFileRef(string Domain, int ModId, int FileId, string? Key, long Expires);
public sealed record NxmCollectionRef(string Domain, string Slug, int Revision, string? Key, long Expires);

public static class NxmUriParser
{
    /// <summary>Legacy mod-file overload. Throws if not a mod-file shape.</summary>
    public static (string Domain, int ModId, int FileId, string? Key, long Expires) Parse(string nxmUri)
    {
        if (TryParseModFile(nxmUri, out var r) && r != null)
            return (r.Domain, r.ModId, r.FileId, r.Key, r.Expires);
        throw new ArgumentException("Not a mod-file nxm URI", nameof(nxmUri));
    }

    public static NxmKind Classify(string nxmUri)
    {
        try
        {
            var uri = new Uri(nxmUri);
            if (!string.Equals(uri.Scheme, "nxm", StringComparison.OrdinalIgnoreCase)) return NxmKind.Unknown;
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length >= 1 && parts[0].Equals("collections", StringComparison.OrdinalIgnoreCase))
                return NxmKind.Collection;
            if (parts.Length >= 1 && parts[0].Equals("mods", StringComparison.OrdinalIgnoreCase))
                return NxmKind.ModFile;
            return NxmKind.Unknown;
        }
        catch { return NxmKind.Unknown; }
    }

    public static bool TryParseModFile(string nxmUri, out NxmModFileRef? result)
    {
        result = null;
        try
        {
            var uri = new Uri(nxmUri);
            if (!string.Equals(uri.Scheme, "nxm", StringComparison.OrdinalIgnoreCase)) return false;
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            // expected: mods/<modId>/files/<fileId>
            if (parts.Length < 4) return false;
            if (!parts[0].Equals("mods", StringComparison.OrdinalIgnoreCase)) return false;
            if (!int.TryParse(parts[1], out var modId)) return false;
            if (!parts[2].Equals("files", StringComparison.OrdinalIgnoreCase)) return false;
            if (!int.TryParse(parts[3], out var fileId)) return false;
            var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
            long.TryParse(q["expires"], out var expires);
            result = new NxmModFileRef(uri.Host, modId, fileId, q["key"], expires);
            return true;
        }
        catch { return false; }
    }

    public static bool TryParseCollection(string nxmUri, out NxmCollectionRef? result)
    {
        result = null;
        try
        {
            var uri = new Uri(nxmUri);
            if (!string.Equals(uri.Scheme, "nxm", StringComparison.OrdinalIgnoreCase)) return false;
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            // expected: collections/<slug>/revisions/<rev>
            if (parts.Length < 4) return false;
            if (!parts[0].Equals("collections", StringComparison.OrdinalIgnoreCase)) return false;
            var slug = parts[1];
            if (string.IsNullOrEmpty(slug)) return false;
            if (!parts[2].Equals("revisions", StringComparison.OrdinalIgnoreCase)) return false;
            if (!int.TryParse(parts[3], out var revision)) return false;
            var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
            long.TryParse(q["expires"], out var expires);
            result = new NxmCollectionRef(uri.Host, slug, revision, q["key"], expires);
            return true;
        }
        catch { return false; }
    }
}
