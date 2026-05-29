using System.Collections.Generic;

namespace CPMM2067.Core.Mods;

public enum ModEnabled
{
    Enabled,
    Disabled,
}

public sealed record ModInstallationState
{
    public required ModManifest Manifest { get; init; }
    public required ModEnabled State { get; init; }
    public required IReadOnlyList<InstalledFileRecord> Files { get; init; }
    public int LoadOrder { get; init; }
}
