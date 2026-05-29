using System;

namespace CPMM2067.Core.Backups;

public sealed record BackupRecord
{
    public required string RelativePath { get; init; }
    public required string BackupAbsolutePath { get; init; }
    public required string OriginalSha256 { get; init; }
    public required long OriginalSizeBytes { get; init; }
    public required string GameVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
