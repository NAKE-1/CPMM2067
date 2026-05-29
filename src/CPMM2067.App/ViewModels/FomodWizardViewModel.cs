using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CPMM2067.Archives.Fomod;

namespace CPMM2067.App.ViewModels;

public sealed class FomodWizardViewModel
{
    public string ModuleName { get; }
    public ObservableCollection<FomodStepGroup> Groups { get; } = new();

    public FomodWizardViewModel(FomodPlan plan, string moduleConfigPath, string extractedRoot)
    {
        ModuleName = plan.ModuleName;

        // Re-parse the XML to surface the option tree (FomodParser.Resolve already auto-selected;
        // we need the raw tree to let the user choose).
        var doc = System.Xml.Linq.XDocument.Load(moduleConfigPath);
        var root = doc.Root!;
        var ns = root.GetDefaultNamespace();
        System.Xml.Linq.XName Q(string n) => ns + n;

        var steps = root.Element(Q("installSteps"))?.Elements(Q("installStep"))
                    ?? Enumerable.Empty<System.Xml.Linq.XElement>();
        foreach (var step in steps)
        {
            var stepName = (string?)step.Attribute("name") ?? "step";
            var groups = step.Element(Q("optionalFileGroups"))?.Elements(Q("group"))
                        ?? Enumerable.Empty<System.Xml.Linq.XElement>();
            foreach (var group in groups)
            {
                var g = new FomodStepGroup
                {
                    StepName = stepName,
                    GroupName = (string?)group.Attribute("name") ?? "group",
                    GroupType = (string?)group.Attribute("type") ?? "SelectAny",
                };
                var plugins = group.Element(Q("plugins"))?.Elements(Q("plugin")).ToList() ?? new();
                foreach (var p in plugins)
                {
                    var typeAttr = (string?)p.Element(Q("typeDescriptor"))?.Element(Q("type"))?.Attribute("name") ?? "";
                    var pickedByDefault = typeAttr.Equals("Recommended", System.StringComparison.OrdinalIgnoreCase)
                                       || typeAttr.Equals("Required", System.StringComparison.OrdinalIgnoreCase);
                    g.Options.Add(new FomodOption
                    {
                        Name = (string?)p.Attribute("name") ?? "plugin",
                        Description = (string?)p.Element(Q("description")) ?? "",
                        TypeHint = typeAttr,
                        IsSelected = pickedByDefault,
                        XmlElement = p,
                    });
                }

                // Enforce exclusivity for SelectExactlyOne when nothing picked: pick first
                if (g.IsExclusive && g.Options.All(o => !o.IsSelected) && g.Options.Count > 0)
                    g.Options[0].IsSelected = true;

                Groups.Add(g);
            }
        }
    }

    public List<FomodFile> ResolveSelectedFiles(string extractedRoot)
    {
        var doc = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(extractedRoot, "fomod", "ModuleConfig.xml"));
        var ns = doc.Root!.GetDefaultNamespace();
        System.Xml.Linq.XName Q(string n) => ns + n;

        var result = new List<FomodFile>();

        // required files
        var required = doc.Root!.Element(Q("requiredInstallFiles"))?.Element(Q("files"));
        if (required != null) AddFromFilesElement(required, result, "required");

        // user selections
        foreach (var g in Groups)
        {
            foreach (var opt in g.Options.Where(o => o.IsSelected))
            {
                var files = opt.XmlElement.Element(Q("files"));
                if (files != null) AddFromFilesElement(files, result, $"{g.StepName} / {opt.Name}");
            }
        }

        foreach (var f in result)
            f.SourceAbsolute = System.IO.Path.Combine(extractedRoot, f.Source.Replace('/', System.IO.Path.DirectorySeparatorChar));
        return result;

        void AddFromFilesElement(System.Xml.Linq.XElement filesEl, List<FomodFile> sink, string reason)
        {
            foreach (var node in filesEl.Elements())
            {
                var src = (string?)node.Attribute("source");
                if (string.IsNullOrEmpty(src)) continue;
                sink.Add(new FomodFile
                {
                    Source = src,
                    Destination = (string?)node.Attribute("destination") ?? "",
                    IsFolder = node.Name.LocalName.Equals("folder", System.StringComparison.OrdinalIgnoreCase),
                    Reason = reason,
                });
            }
        }
    }
}

public sealed partial class FomodStepGroup : ObservableObject
{
    public string StepName { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string GroupType { get; set; } = "SelectAny";
    public ObservableCollection<FomodOption> Options { get; } = new();
    public bool IsExclusive => GroupType is "SelectExactlyOne" or "SelectAtMostOne";
    public string Heading => $"{StepName} / {GroupName}  [{GroupType}]";
}

public sealed partial class FomodOption : ObservableObject
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string TypeHint { get; set; } = "";
    [ObservableProperty] private bool _isSelected;
    public System.Xml.Linq.XElement XmlElement { get; set; } = null!;
}
