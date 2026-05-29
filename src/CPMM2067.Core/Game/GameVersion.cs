using System;

namespace CPMM2067.Core.Game;

public readonly record struct GameVersion(int Major, int Minor, int Patch, int Build, string Raw)
{
    public static GameVersion Unknown { get; } = new(0, 0, 0, 0, "unknown");

    public override string ToString() => Raw;

    public bool IsAtLeast(GameVersion other) =>
        (Major, Minor, Patch, Build).CompareTo((other.Major, other.Minor, other.Patch, other.Build)) >= 0;

    public static bool TryParse(string? input, out GameVersion version)
    {
        version = Unknown;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var parts = input.Split(new[] { '.', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        int p(int i) => i < parts.Length && int.TryParse(parts[i], out var v) ? v : 0;
        version = new GameVersion(p(0), p(1), p(2), p(3), input);
        return true;
    }
}
