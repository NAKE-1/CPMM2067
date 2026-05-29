using System.IO;
using System.Threading.Tasks;
using CPMM2067.Core.Mods;
using CPMM2067.Frameworks.Cet;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CPMM2067.Tests;

public class CetHandlerTests
{
    [Fact]
    public async Task Install_then_uninstall_round_trip()
    {
        var gameDir = TestEnvironment.CreateTempDir("game-cet");
        var game = TestEnvironment.BuildFakeGame(gameDir);

        var extracted = TestEnvironment.CreateTempDir("cet-src");
        var modSrc = Path.Combine(extracted, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "fakecet");
        Directory.CreateDirectory(modSrc);
        File.WriteAllText(Path.Combine(modSrc, "init.lua"), "-- fake init");
        File.WriteAllText(Path.Combine(modSrc, "config.lua"), "return {}");

        var handler = new CetHandler(NullLogger<CetHandler>.Instance);

        (await handler.DetectAsync(extracted)).Should().Be(ModFramework.Cet);

        var state = await handler.InstallAsync(new ModInstallationRequest
        {
            ExtractedRootDir = extracted,
            SuggestedName = "fakecet",
            Version = "1.0",
        }, game);

        var dst = Path.Combine(gameDir, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "fakecet");
        Directory.EnumerateFiles(dst).Should().HaveCount(2);

        await handler.UninstallAsync(state, game);
        File.Exists(Path.Combine(dst, "init.lua")).Should().BeFalse();
    }
}
