namespace CPMM2067.Core.Mods;

public sealed record InstalledFileRecord
{
    public required ModId OwnerMod { get; init; }
    public required string RelativePath { get; init; }
    public required string Sha256 { get; init; }
    public required long SizeBytes { get; init; }
    public bool OverwroteVanilla { get; init; }
    public string? BackupRelativePath { get; init; }
}
