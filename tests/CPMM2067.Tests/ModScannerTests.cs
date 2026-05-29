using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CPMM2067.Core.Mods;
using CPMM2067.Frameworks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CPMM2067.Tests;

public class ModScannerTests
{
    [Fact]
    public async Task Scans_all_seven_frameworks()
    {
        var gameDir = TestEnvironment.CreateTempDir("scanner");
        var game = TestEnvironment.BuildFakeGame(gameDir);

        // 1. REDmod
        Directory.CreateDirectory(Path.Combine(gameDir, "mods", "redmod1"));
        File.WriteAllText(Path.Combine(gameDir, "mods", "redmod1", "info.json"),
            "{\"name\":\"redmod1\",\"version\":\"1.0\"}");
        // 2. legacy archive
        Directory.CreateDirectory(Path.Combine(gameDir, "archive", "pc", "mod"));
        File.WriteAllText(Path.Combine(gameDir, "archive", "pc", "mod", "x.archive"), "bin");
        // 3. archiveXL
        File.WriteAllText(Path.Combine(gameDir, "archive", "pc", "mod", "x.xl"), "yaml");
        // 4. RED4ext
        var r4x = Path.Combine(gameDir, "red4ext", "plugins", "plug");
        Directory.CreateDirectory(r4x);
        File.WriteAllText(Path.Combine(r4x, "plug.dll"), "bin");
        // 5. CET
        var cet = Path.Combine(gameDir, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "cetmod");
        Directory.CreateDirectory(cet);
        File.WriteAllText(Path.Combine(cet, "init.lua"), "");
        // 6. tweakXL
        Directory.CreateDirectory(Path.Combine(gameDir, "r6", "tweaks"));
        File.WriteAllText(Path.Combine(gameDir, "r6", "tweaks", "t.yaml"), "");
        // 7. redscript
        var reds = Path.Combine(gameDir, "r6", "scripts", "MyMod");
        Directory.CreateDirectory(reds);
        File.WriteAllText(Path.Combine(reds, "main.reds"), "");

        var scanner = new ModScanner(NullLogger<ModScanner>.Instance);
        var found = await scanner.ScanAsync(game);

        found.Should().Contain(f => f.Framework == ModFramework.RedMod);
        found.Should().Contain(f => f.Framework == ModFramework.LegacyArchive);
        found.Should().Contain(f => f.Framework == ModFramework.ArchiveXL);
        found.Should().Contain(f => f.Framework == ModFramework.Red4ext);
        found.Should().Contain(f => f.Framework == ModFramework.Cet);
        found.Should().Contain(f => f.Framework == ModFramework.TweakXL);
        found.Should().Contain(f => f.Framework == ModFramework.Redscript);
        found.Count(f => f.Framework == ModFramework.RedMod).Should().BeGreaterThanOrEqualTo(1);
    }
}
