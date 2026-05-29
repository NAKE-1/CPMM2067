using System.IO;
using System.Text.Json;
using CPMM2067.Core;

namespace CPMM2067.App.Services;

public sealed record AppSettings
{
    public bool TelemetryEnabled { get; init; }
    public bool FirstLaunchCompleted { get; init; }
    public string? NexusApiKey { get; init; }
    public string? ManualGamePath { get; init; }
    public bool TestingMode { get; init; }
    public bool AutoScanOnStartup { get; init; } = true;
    public string? LastKnownGameVersion { get; init; }
    public string? NexusJwt { get; init; }
    public string? PreferredBrowserExe { get; init; }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions s_opts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        if (!File.Exists(AppPaths.SettingsFile)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        AppPaths.EnsureAll();
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, s_opts));
    }
}
