using System;

namespace CPMM2067.Core.Mods;

public readonly record struct ModId(Guid Value)
{
    public static ModId NewId() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
    public static ModId Parse(string s) => new(Guid.Parse(s));
}
