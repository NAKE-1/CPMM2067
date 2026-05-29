using Avalonia;
using CPMM2067.App.Services;
using System;
using System.IO;
using Velopack;

namespace CPMM2067.App;

sealed class Program
{
    public static string? PendingNxmUri { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack's installer-detection MUST be the first line in Main() and
        // must be visible inside this assembly (the verifier scans the entry exe's IL).
        // Don't hoist this into another project — Velopack pack will fatal-error otherwise.
        VelopackApp.Build().Run();

        string? nxm = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--nxm", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                nxm = args[i + 1];
        }

        if (nxm != null)
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "cpmm2067-nxm.log"),
                $"{DateTime.UtcNow:o}  {nxm}{Environment.NewLine}");
        }

        // Single-instance gate: if another CPMM2067 is already running,
        // forward the nxm URI to it via named pipe and exit silently.
        if (!SingleInstance.TryAcquire())
        {
            if (nxm != null)
                SingleInstance.SendToRunningInstance("nxm:" + nxm);
            return;
        }

        PendingNxmUri = nxm;
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            SingleInstance.Release();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
