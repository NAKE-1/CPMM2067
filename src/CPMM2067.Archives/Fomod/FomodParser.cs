using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CPMM2067.Archives.Fomod;

/// <summary>
/// Minimal FOMOD ModuleConfig.xml parser. Resolves "required files" + the default/recommended
/// option from each step group. Does NOT implement the multi-step interactive wizard — that's
/// queued for a later release.
/// </summary>
public static class FomodParser
{
    public static bool IsFomod(string extractedRootDir, out string moduleConfigPath)
    {
        moduleConfigPath = "";
        if (!Directory.Exists(extractedRootDir)) return false;
        var probe = Path.Combine(extractedRootDir, "fomod", "ModuleConfig.xml");
        if (File.Exists(probe)) { moduleConfigPath = probe; return true; }
        // Some packagers nest it
        var nested = Directory.EnumerateFiles(extractedRootDir, "ModuleConfig.xml", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (nested != null) { moduleConfigPath = nested; return true; }
        return false;
    }

    public static FomodPlan Resolve(string moduleConfigPath, string extractedRootDir)
    {
        var doc = XDocument.Load(moduleConfigPath);
        var root = doc.Root ?? throw new InvalidOperationException("Empty ModuleConfig.xml");
        var ns = root.GetDefaultNamespace();
        XName Q(string n) => ns + n;

        var plan = new FomodPlan
        {
            ModuleName = (string?)root.Element(Q("moduleName")) ?? "Unknown FOMOD",
        };

        // requiredInstallFiles
        var required = root.Element(Q("requiredInstallFiles"));
        if (required != null)
        {
            foreach (var f in EnumerateFileNodes(required, Q))
                plan.Files.Add(f.WithReason("required"));
        }

        // installSteps → installStep → optionalFileGroups → group → plugins → plugin
        var steps = root.Element(Q("installSteps"))?.Elements(Q("installStep")) ?? Enumerable.Empty<XElement>();
        foreach (var step in steps)
        {
            var stepName = (string?)step.Attribute("name") ?? "step";
            var groups = step.Element(Q("optionalFileGroups"))?.Elements(Q("group")) ?? Enumerable.Empty<XElement>();
            foreach (var group in groups)
            {
                var groupName = (string?)group.Attribute("name") ?? "group";
                var groupType = (string?)group.Attribute("type") ?? "SelectAny";
                var plugins = group.Element(Q("plugins"))?.Elements(Q("plugin")).ToList() ?? new();
                if (plugins.Count == 0) continue;

                // Pick the "default" plugin:
                //   1. The one with typeDescriptor → type "Recommended"
                //   2. Otherwise the first
                var chosen = plugins
                    .Select(p => new
                    {
                        Plugin = p,
                        Type = (string?)p.Element(Q("typeDescriptor"))?.Element(Q("type"))?.Attribute("name") ?? ""
                    })
                    .OrderBy(p => p.Type.Equals("Recommended", StringComparison.OrdinalIgnoreCase) ? 0
                                : p.Type.Equals("Required", StringComparison.OrdinalIgnoreCase) ? 0
                                : p.Type.Equals("Optional", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                    .First().Plugin;

                var pluginName = (string?)chosen.Attribute("name") ?? "plugin";
                plan.Decisions.Add($"{stepName} / {groupName} [{groupType}] → {pluginName}");

                foreach (var f in EnumerateFileNodes(chosen, Q))
                    plan.Files.Add(f.WithReason($"step '{stepName}' / '{pluginName}'"));
            }
        }

        // Resolve sources to absolute paths under the extract root
        foreach (var f in plan.Files)
        {
            var srcRel = f.Source.Replace('/', Path.DirectorySeparatorChar);
            f.SourceAbsolute = Path.Combine(extractedRootDir, srcRel);
        }

        return plan;
    }

    private static IEnumerable<FomodFile> EnumerateFileNodes(XElement parent, Func<string, XName> Q)
    {
        // <files><file source="..." destination="..."/> | <folder source="..." destination="..."/></files>
        var files = parent.Element(Q("files"));
        if (files == null) yield break;
        foreach (var node in files.Elements())
        {
            var src = (string?)node.Attribute("source");
            if (string.IsNullOrEmpty(src)) continue;
            var dst = (string?)node.Attribute("destination") ?? string.Empty;
            var isDir = node.Name.LocalName.Equals("folder", StringComparison.OrdinalIgnoreCase);
            yield return new FomodFile { Source = src, Destination = dst, IsFolder = isDir };
        }
    }
}

public sealed class FomodPlan
{
    public string ModuleName { get; set; } = "";
    public List<string> Decisions { get; } = new();
    public List<FomodFile> Files { get; } = new();
}

public sealed class FomodFile
{
    public string Source { get; set; } = "";
    public string SourceAbsolute { get; set; } = "";
    public string Destination { get; set; } = "";
    public bool IsFolder { get; set; }
    public string Reason { get; set; } = "";

    public FomodFile WithReason(string r) { Reason = r; return this; }
}
