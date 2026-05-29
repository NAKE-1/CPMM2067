using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CPMM2067.Backup;
using CPMM2067.Core.Mods;
using CPMM2067.Frameworks.RedMod;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CPMM2067.Tests;

public class RedModHandlerTests
{
    [Fact]
    public async Task Install_then_uninstall_leaves_game_dir_clean()
    {
        var gameDir = TestEnvironment.CreateTempDir("game-rm");
        var game = TestEnvironment.BuildFakeGame(gameDir);
        var extracted = TestEnvironment.BuildFakeRedModZipExtractedDir("mytestmod");

        var handler = new RedModHandler(
            NullLogger<RedModHandler>.Instance,
            new FileBackupStore(NullLogger<FileBackupStore>.Instance));

        var state = await handler.InstallAsync(new ModInstallationRequest
        {
            ExtractedRootDir = extracted,
            SuggestedName = "mytestmod",
            Version = "1.0.0",
        }, game);

        var installedInfoJson = Path.Combine(gameDir, "mods", "mytestmod", "info.json");
        File.Exists(installedInfoJson).Should().BeTrue();
        state.Files.Should().NotBeEmpty();
        state.Manifest.Framework.Should().Be(ModFramework.RedMod);

        var modsJson = File.ReadAllText(game.ModsJsonPath);
        modsJson.Should().Contain("mytestmod");

        await handler.UninstallAsync(state, game);

        Directory.Exists(Path.Combine(gameDir, "mods", "mytestmod")).Should().BeFalse();
        File.ReadAllText(game.ModsJsonPath).Should().NotContain("mytestmod");
    }
}
