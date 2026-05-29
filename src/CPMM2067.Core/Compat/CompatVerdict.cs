using System.Collections.Generic;

namespace CPMM2067.Core.Compat;

public enum CompatStatus
{
    Unknown,
    Compatible,
    Risky,
    Incompatible,
}

public sealed record CompatVerdict(
    CompatStatus Status,
    string Headline,
    IReadOnlyList<string> Reasons,
    bool UserOverridden = false);
