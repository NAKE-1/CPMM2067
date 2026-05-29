using System.IO;
using System.Threading.Tasks;
using CPMM2067.Core.Mods;
using CPMM2067.Frameworks.Red4ext;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CPMM2067.Tests;

public class Red4extHandlerTests
{
    [Fact]
    public async Task Install_then_uninstall_round_trip()
    {
        var gameDir = TestEnvironment.CreateTempDir("game-r4x");
        var game = TestEnvironment.BuildFakeGame(gameDir);

        var extracted = TestEnvironment.CreateTempDir("r4x-src");
        var pluginDir = Path.Combine(extracted, "red4ext", "plugins", "FakePlugin");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "FakePlugin.dll"), "binary placeholder");

        var handler = new Red4extHandler(NullLogger<Red4extHandler>.Instance);

        var fw = await handler.DetectAsync(extracted);
        fw.Should().Be(ModFramework.Red4ext);

        var state = await handler.InstallAsync(new ModInstallationRequest
        {
            ExtractedRootDir = extracted,
            SuggestedName = "FakePlugin",
            Version = "1.0",
        }, game);

        var installed = Path.Combine(gameDir, "red4ext", "plugins", "FakePlugin", "FakePlugin.dll");
        File.Exists(installed).Should().BeTrue();
        state.Files.Should().NotBeEmpty();
        state.Manifest.Framework.Should().Be(ModFramework.Red4ext);

        await handler.UninstallAsync(state, game);
        File.Exists(installed).Should().BeFalse();
    }
}
