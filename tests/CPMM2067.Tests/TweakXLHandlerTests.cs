using System.IO;
using System.Threading.Tasks;
using CPMM2067.Core.Mods;
using CPMM2067.Frameworks.TweakXL;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CPMM2067.Tests;

public class TweakXLHandlerTests
{
    [Fact]
    public async Task Install_then_uninstall_round_trip()
    {
        var gameDir = TestEnvironment.CreateTempDir("game-txl");
        var game = TestEnvironment.BuildFakeGame(gameDir);

        var extracted = TestEnvironment.CreateTempDir("txl-src");
        var tweaksDir = Path.Combine(extracted, "r6", "tweaks");
        Directory.CreateDirectory(tweaksDir);
        File.WriteAllText(Path.Combine(tweaksDir, "fake.yaml"), "Items.Foo:\n  displayName: bar");

        var handler = new TweakXLHandler(NullLogger<TweakXLHandler>.Instance);

        var fw = await handler.DetectAsync(extracted);
        fw.Should().Be(ModFramework.TweakXL);

        var state = await handler.InstallAsync(new ModInstallationRequest
        {
            ExtractedRootDir = extracted,
            SuggestedName = "txltest",
            Version = "1.0",
        }, game);

        var installed = Path.Combine(gameDir, "r6", "tweaks", "fake.yaml");
        File.Exists(installed).Should().BeTrue();

        await handler.UninstallAsync(state, game);
        File.Exists(installed).Should().BeFalse();
    }
}
