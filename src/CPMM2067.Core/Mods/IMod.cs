using System.Collections.Generic;

namespace CPMM2067.Core.Mods;

public interface IMod
{
    ModManifest Manifest { get; }
    IReadOnlyList<InstalledFileRecord> Files { get; }
    ModEnabled State { get; }
    int LoadOrder { get; }
}
