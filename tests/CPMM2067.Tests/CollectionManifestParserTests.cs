using System.IO;
using System.IO.Compression;
using CPMM2067.Nexus;
using FluentAssertions;
using Xunit;

namespace CPMM2067.Tests;

public class CollectionManifestParserTests
{
    [Fact]
    public void Parses_inline_mods_array()
    {
        var json = @"{
            ""info"": { ""name"": ""TestCol"", ""author"": ""me"", ""revisionNumber"": 3 },
            ""mods"": [
              { ""name"": ""ModA"", ""optional"": false, ""source"": { ""modId"": 100, ""fileId"": 200 } },
              { ""name"": ""ModB"", ""optional"": true,  ""source"": { ""modId"": 101, ""fileId"": 201 } }
            ]
        }";
        var c = CollectionManifestParser.Parse(json, "fallback");
        c.Name.Should().Be("TestCol");
        c.Author.Should().Be("me");
        c.Revision.Should().Be(3);
        c.Mods.Should().HaveCount(2);
        c.Mods[0].Optional.Should().BeFalse();
        c.Mods[1].Optional.Should().BeTrue();
        c.Mods[0].ModId.Should().Be(100);
    }

    [Fact]
    public void Loads_manifest_from_zip()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"col-{System.Guid.NewGuid():N}.zip");
        using (var fs = File.Create(tmp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("collection_data.json");
            using var ew = new StreamWriter(entry.Open());
            ew.Write(@"{""info"":{""name"":""FromZip""},""mods"":[{""name"":""X"",""source"":{""modId"":1,""fileId"":2}}]}");
        }
        var c = CollectionManifestParser.LoadFromFile(tmp);
        c.Name.Should().Be("FromZip");
        c.Mods.Should().HaveCount(1);
        File.Delete(tmp);
    }
}
