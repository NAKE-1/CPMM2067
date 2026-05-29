using CPMM2067.Core.Game;
using FluentAssertions;
using Xunit;

namespace CPMM2067.Tests;

public class GameVersionTests
{
    [Theory]
    [InlineData("2.21.0.0", 2, 21, 0, 0)]
    [InlineData("2.21", 2, 21, 0, 0)]
    [InlineData("1.6.1+build", 1, 6, 1, 0)]
    public void Parses_dotted_versions(string raw, int maj, int min, int patch, int build)
    {
        GameVersion.TryParse(raw, out var v).Should().BeTrue();
        v.Major.Should().Be(maj);
        v.Minor.Should().Be(min);
        v.Patch.Should().Be(patch);
        v.Build.Should().Be(build);
    }

    [Fact]
    public void IsAtLeast_compares_lexicographically()
    {
        GameVersion.TryParse("2.21.0.0", out var current);
        GameVersion.TryParse("2.20.0.0", out var older);
        current.IsAtLeast(older).Should().BeTrue();
        older.IsAtLeast(current).Should().BeFalse();
    }
}
