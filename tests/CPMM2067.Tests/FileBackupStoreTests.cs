using System.IO;
using System.Threading.Tasks;
using CPMM2067.Backup;
using CPMM2067.Core.Mods;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CPMM2067.Tests;

public class FileBackupStoreTests
{
    [Fact]
    public async Task Backup_then_restore_yields_byte_identical_original()
    {
        var gameDir = TestEnvironment.CreateTempDir("game");
        var game = TestEnvironment.BuildFakeGame(gameDir, "2.21");

        var relPath = Path.Combine("r6", "scripts", "vanilla.reds");
        var absPath = Path.Combine(gameDir, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);
        var originalBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        File.WriteAllBytes(absPath, originalBytes);

        var store = new FileBackupStore(NullLogger<FileBackupStore>.Instance);
        var record = await store.BackupIfVanillaAsync(game, relPath, ModId.NewId());

        record.Should().NotBeNull();
        File.WriteAllBytes(absPath, new byte[] { 99 });

        await store.RestoreAsync(game, record!);
        File.ReadAllBytes(absPath).Should().Equal(originalBytes);
    }

    [Fact]
    public async Task Backup_returns_null_when_file_does_not_exist()
    {
        var gameDir = TestEnvironment.CreateTempDir("game-missing");
        var game = TestEnvironment.BuildFakeGame(gameDir, "2.21");
        var store = new FileBackupStore(NullLogger<FileBackupStore>.Instance);

        var record = await store.BackupIfVanillaAsync(game, "does/not/exist.reds", ModId.NewId());
        record.Should().BeNull();
    }
}
