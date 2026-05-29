using System.Collections.Generic;
using System.Threading.Tasks;
using CPMM2067.Archives.Fomod;

namespace CPMM2067.Frameworks.Fomod;

/// <summary>
/// Optional callback for ModInstaller to surface a FOMOD wizard UI before applying.
/// Return null to fall back to the auto-default plan; return an empty list to abort.
/// </summary>
public interface IFomodChooser
{
    Task<List<FomodFile>?> ChooseAsync(string moduleConfigPath, string extractedRoot);
}
