using System;
using System.Collections.Generic;

namespace CPMM2067.Core.Mods;

public sealed record ModManifest
{
    public required ModId Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Version { get; init; }
    public string? Author { get; init; }
    public required ModFramework Framework { get; init; }
    public ModSource Source { get; init; } = ModSource.LocalFile;
    public int? NexusModId { get; init; }
    public int? NexusFileId { get; init; }
    public string? NexusGameDomain { get; init; }
    public string? SupportedGameVersion { get; init; }
    public IReadOnlyList<string> RequiredFrameworks { get; init; } = Array.Empty<string>();
    public DateTimeOffset InstalledAt { get; init; } = DateTimeOffset.UtcNow;
    public string OriginalArchivePath { get; init; } = string.Empty;
    public string OriginalArchiveSha256 { get; init; } = string.Empty;
}
