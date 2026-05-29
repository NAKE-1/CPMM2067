using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CPMM2067.Core.Game;

namespace CPMM2067.Core.Mods;

public interface IModFrameworkHandler
{
    ModFramework Framework { get; }

    bool SupportsLoadOrder { get; }

    Task<ModFramework> DetectAsync(string extractedRootDir, CancellationToken ct = default);

    Task<ModInstallationState> InstallAsync(
        ModInstallationRequest request,
        GameInstallation game,
        CancellationToken ct = default);

    Task UninstallAsync(
        ModInstallationState state,
        GameInstallation game,
        CancellationToken ct = default);

    Task SetEnabledAsync(
        ModInstallationState state,
        ModEnabled target,
        GameInstallation game,
        CancellationToken ct = default);

    Task<IReadOnlyList<ModInstallationState>> ReorderAsync(
        IReadOnlyList<ModInstallationState> ordered,
        GameInstallation game,
        CancellationToken ct = default);

    Task DeployAsync(GameInstallation game, CancellationToken ct = default);
}

public sealed record ModInstallationRequest
{
    public required string ExtractedRootDir { get; init; }
    public required string SuggestedName { get; init; }
    public required string Version { get; init; }
    public string? Author { get; init; }
    public ModSource Source { get; init; } = ModSource.LocalFile;
    public int? NexusModId { get; init; }
    public int? NexusFileId { get; init; }
    public string? NexusGameDomain { get; init; }
    public string OriginalArchivePath { get; init; } = string.Empty;
    public string OriginalArchiveSha256 { get; init; } = string.Empty;
}
