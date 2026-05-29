using System.IO;
using System.Linq;
using CPMM2067.Core.Mods;

namespace CPMM2067.Frameworks.Common;

public static class FrameworkDetector
{
    public static ModFramework Detect(string extractedRootDir)
    {
        if (!Directory.Exists(extractedRootDir)) return ModFramework.Unknown;

        if (Directory.EnumerateFiles(extractedRootDir, "info.json", SearchOption.AllDirectories)
                .Any(p => p.Contains(Path.DirectorySeparatorChar + "mods" + Path.DirectorySeparatorChar)
                       || Path.GetDirectoryName(p)?.Equals(extractedRootDir) == true))
        {
            if (Directory.EnumerateFiles(extractedRootDir, "info.json", SearchOption.AllDirectories).Any())
                return ModFramework.RedMod;
        }

        if (Directory.Exists(Path.Combine(extractedRootDir, "mods"))) return ModFramework.RedMod;
        if (Directory.Exists(Path.Combine(extractedRootDir, "archive"))) return ModFramework.LegacyArchive;
        if (Directory.Exists(Path.Combine(extractedRootDir, "red4ext"))) return ModFramework.Red4ext;
        if (Directory.Exists(Path.Combine(extractedRootDir, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods")))
            return ModFramework.Cet;
        if (Directory.Exists(Path.Combine(extractedRootDir, "r6", "tweaks"))) return ModFramework.TweakXL;
        if (Directory.Exists(Path.Combine(extractedRootDir, "r6", "scripts"))) return ModFramework.Redscript;

        return ModFramework.Unknown;
    }
}
