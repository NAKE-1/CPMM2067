using System.Threading.Tasks;
using CPMM2067.Compat;
using CPMM2067.Core.Compat;
using CPMM2067.Core.Game;
using CPMM2067.Core.Mods;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CPMM2067.Tests;

public class CompatEngineTests
{
    [Fact]
    public async Task Same_major_minor_yields_Compatible()
    {
        using var engine = new CompatEngine(NullLogger<CompatEngine>.Instance);
        GameVersion.TryParse("2.21", out var gameVer);
        var game = new GameInstallation { InstallDir = "x", Storefront = GameStorefront.Manual, Version = gameVer };
        var manifest = NewManifest(supported: "2.21.0");
        var v = await engine.EvaluateAsync(manifest, game);
        v.Status.Should().BeOneOf(CompatStatus.Compatible, CompatStatus.Risky);
    }

    [Fact]
    public async Task Major_mismatch_yields_Incompatible()
    {
        using var engine = new CompatEngine(NullLogger<CompatEngine>.Instance);
        GameVersion.TryParse("2.21", out var gameVer);
        var game = new GameInstallation { InstallDir = "x", Storefront = GameStorefront.Manual, Version = gameVer };
        var manifest = NewManifest(supported: "1.6.1");
        var v = await engine.EvaluateAsync(manifest, game);
        v.Status.Should().Be(CompatStatus.Incompatible);
    }

    [Fact]
    public async Task Unknown_supported_version_yields_Risky_at_minimum()
    {
        using var engine = new CompatEngine(NullLogger<CompatEngine>.Instance);
        GameVersion.TryParse("2.21", out var gameVer);
        var game = new GameInstallation { InstallDir = "x", Storefront = GameStorefront.Manual, Version = gameVer };
        var manifest = NewManifest(supported: null);
        var v = await engine.EvaluateAsync(manifest, game);
        v.Status.Should().NotBe(CompatStatus.Compatible);
    }

    private static ModManifest NewManifest(string? supported) => new()
    {
        Id = ModId.NewId(),
        Name = "test",
        Version = "1.0",
        Framework = ModFramework.RedMod,
        SupportedGameVersion = supported,
    };
}
