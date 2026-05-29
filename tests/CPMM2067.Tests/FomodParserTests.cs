using System.IO;
using CPMM2067.Archives.Fomod;
using FluentAssertions;
using Xunit;

namespace CPMM2067.Tests;

public class FomodParserTests
{
    [Fact]
    public void Detects_and_resolves_simple_fomod()
    {
        var dir = TestEnvironment.CreateTempDir("fomod");
        var fomodDir = Path.Combine(dir, "fomod");
        Directory.CreateDirectory(fomodDir);
        File.WriteAllText(Path.Combine(fomodDir, "ModuleConfig.xml"), @"<?xml version=""1.0""?>
<config>
  <moduleName>FakeFomod</moduleName>
  <requiredInstallFiles>
    <files>
      <file source=""common/required.txt"" destination=""mods/Fake/required.txt"" />
    </files>
  </requiredInstallFiles>
  <installSteps>
    <installStep name=""Step 1"">
      <optionalFileGroups>
        <group name=""Body"" type=""SelectExactlyOne"">
          <plugins>
            <plugin name=""Slim"">
              <files>
                <file source=""body/slim.txt"" destination=""mods/Fake/body.txt"" />
              </files>
              <typeDescriptor><type name=""Recommended""/></typeDescriptor>
            </plugin>
            <plugin name=""Thicc"">
              <files>
                <file source=""body/thicc.txt"" destination=""mods/Fake/body.txt"" />
              </files>
              <typeDescriptor><type name=""Optional""/></typeDescriptor>
            </plugin>
          </plugins>
        </group>
      </optionalFileGroups>
    </installStep>
  </installSteps>
</config>");
        Directory.CreateDirectory(Path.Combine(dir, "common"));
        File.WriteAllText(Path.Combine(dir, "common", "required.txt"), "x");
        Directory.CreateDirectory(Path.Combine(dir, "body"));
        File.WriteAllText(Path.Combine(dir, "body", "slim.txt"), "x");
        File.WriteAllText(Path.Combine(dir, "body", "thicc.txt"), "x");

        FomodParser.IsFomod(dir, out var moduleConfig).Should().BeTrue();
        var plan = FomodParser.Resolve(moduleConfig, dir);
        plan.ModuleName.Should().Be("FakeFomod");
        plan.Files.Should().HaveCount(2);
        plan.Decisions.Should().ContainSingle(d => d.Contains("Slim"));
    }
}
