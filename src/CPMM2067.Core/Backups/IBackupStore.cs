using System.Threading;
using System.Threading.Tasks;
using CPMM2067.Core.Game;
using CPMM2067.Core.Mods;

namespace CPMM2067.Core.Backups;

public interface IBackupStore
{
    Task<BackupRecord?> BackupIfVanillaAsync(
        GameInstallation game,
        string relativePath,
        ModId owningMod,
        CancellationToken ct = default);

    Task RestoreAsync(
        GameInstallation game,
        BackupRecord record,
        CancellationToken ct = default);
}
