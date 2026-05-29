using System.IO;
using System.Linq;
using CPMM2067.Diagnostics;
using FluentAssertions;
using Xunit;

namespace CPMM2067.Tests;

public class LoadedModParserTests
{
    [Fact]
    public void Parses_synthetic_CET_log()
    {
        var gameDir = TestEnvironment.CreateTempDir("ldcet");
        var game = TestEnvironment.BuildFakeGame(gameDir);
        var logDir = Path.Combine(gameDir, "bin", "x64", "plugins", "cyber_engine_tweaks");
        Directory.CreateDirectory(logDir);
        File.WriteAllText(Path.Combine(logDir, "cyber_engine_tweaks.log"),
            "[info] Loading mod 'AlphaMod'\n" +
            "[info] Mod 'AlphaMod' (v1.2) loaded\n" +
            "[error] Failed to load mod 'BetaMod': syntax error\n");

        var parsed = LoadedModParser.ParseCet(game).ToList();
        parsed.Should().Contain(m => m.Name == "AlphaMod" && m.Status == LoadStatus.Loaded);
        parsed.Should().Contain(m => m.Name == "BetaMod" && m.Status == LoadStatus.Failed);
    }

    [Fact]
    public void Parses_synthetic_REDmod_metadata()
    {
        var gameDir = TestEnvironment.CreateTempDir("ldredmod");
        var game = TestEnvironment.BuildFakeGame(gameDir);
        var metaDir = Path.Combine(gameDir, "tools", "redmod");
        Directory.CreateDirectory(metaDir);
        File.WriteAllText(Path.Combine(metaDir, "metadata.json"),
            "{\"mods\":[{\"name\":\"FakeRedmod\",\"version\":\"2.0\"},{\"folder\":\"FolderOnly\"}]}");

        var parsed = LoadedModParser.ParseRedModMetadata(game).ToList();
        parsed.Should().HaveCount(2);
        parsed.Should().Contain(m => m.Name == "FakeRedmod" && m.Version == "2.0");
    }
}
