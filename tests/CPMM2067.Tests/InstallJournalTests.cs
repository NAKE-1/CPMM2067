using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CPMM2067.Core.Mods;
using CPMM2067.Frameworks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CPMM2067.Tests;

public class InstallJournalTests
{
    [Fact]
    public async Task Save_load_update_round_trip()
    {
        // Force the journal into a temp folder for the duration of this test
        var fakeData = TestEnvironment.CreateTempDir("journal");
        // We can't override AppPaths.JournalDir easily without DI; use the real path
        // and clean up afterwards.
        var beforeCount = (Directory.Exists(InstallJournal.JournalDir)
            ? Directory.EnumerateFiles(InstallJournal.JournalDir, "*.json").Count()
            : 0);

        var journal = new InstallJournal(NullLogger<InstallJournal>.Instance);
        var entry = new InstallEntry
        {
            Name = "journalTest_" + System.Guid.NewGuid().ToString("N")[..8],
            Framework = ModFramework.RedMod,
            SourceArchivePath = "x.zip",
            Status = InstallEntryStatus.Installed,
            RelativePaths = new() { "mods/foo/info.json" },
        };
        await journal.SaveAsync(entry);

        var all = journal.LoadAll().Where(t => t.Entry.Name == entry.Name).ToList();
        all.Should().HaveCount(1);
        all[0].Entry.Status.Should().Be(InstallEntryStatus.Installed);

        await journal.UpdateStatusAsync(all[0].Path, InstallEntryStatus.Reverted);

        var re = journal.LoadAll().FirstOrDefault(t => t.Entry.Name == entry.Name);
        re.Entry!.Status.Should().Be(InstallEntryStatus.Reverted);

        // Cleanup
        File.Delete(all[0].Path);
    }
}
