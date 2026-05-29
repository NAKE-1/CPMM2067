using System.Collections.Generic;
using CPMM2067.Core.Mods;

namespace CPMM2067.Core.Profiles;

public sealed record ProfileState
{
    public required string Name { get; init; }
    public IReadOnlyList<ModInstallationState> Mods { get; init; } = new List<ModInstallationState>();
}
