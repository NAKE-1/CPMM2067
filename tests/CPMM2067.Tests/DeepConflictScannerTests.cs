using System.IO;
using CPMM2067.Conflicts;
using FluentAssertions;
using Xunit;

namespace CPMM2067.Tests;

public class DeepConflictScannerTests
{
    [Fact]
    public void Finds_TweakXL_key_overlap_across_files()
    {
        var gameDir = TestEnvironment.CreateTempDir("deep-txl");
        var game = TestEnvironment.BuildFakeGame(gameDir);
        var tweaks = Path.Combine(gameDir, "r6", "tweaks");
        Directory.CreateDirectory(tweaks);
        File.WriteAllText(Path.Combine(tweaks, "a.yaml"), "Items.Foo:\n  displayName: A\n");
        File.WriteAllText(Path.Combine(tweaks, "b.yaml"), "Items.Foo:\n  displayName: B\n");
        File.WriteAllText(Path.Combine(tweaks, "c.yaml"), "Items.Bar:\n  displayName: C\n");

        var report = DeepConflictScanner.Scan(game);
        report.TweakXLKeyConflicts.Should().HaveCount(1);
        report.TweakXLKeyConflicts[0].Key.Should().Be("Items.Foo");
        report.TweakXLKeyConflicts[0].SourceFiles.Should().HaveCount(2);
    }

    [Fact]
    public void Finds_redscript_hook_overlap()
    {
        var gameDir = TestEnvironment.CreateTempDir("deep-reds");
        var game = TestEnvironment.BuildFakeGame(gameDir);
        var scripts = Path.Combine(gameDir, "r6", "scripts");
        Directory.CreateDirectory(Path.Combine(scripts, "ModA"));
        Directory.CreateDirectory(Path.Combine(scripts, "ModB"));
        File.WriteAllText(Path.Combine(scripts, "ModA", "main.reds"),
            "@addMethod(PlayerPuppet)\npublic func DoIt() -> Void { }\n");
        File.WriteAllText(Path.Combine(scripts, "ModB", "main.reds"),
            "@addMethod(PlayerPuppet)\npublic func DoIt() -> Void { LogChannel(n\"x\",\"\"); }\n");

        var report = DeepConflictScanner.Scan(game);
        report.RedscriptHookConflicts.Should().HaveCount(1);
        report.RedscriptHookConflicts[0].Key.Should().Be("addMethod PlayerPuppet.DoIt");
    }
}
