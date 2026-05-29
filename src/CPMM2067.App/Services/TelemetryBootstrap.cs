using System;
using Microsoft.Extensions.Logging;
using Sentry;

namespace CPMM2067.App.Services;

public static class TelemetryBootstrap
{
    private const string DefaultDsn = "";
    private static IDisposable? s_sentry;

    public static void StartIfEnabled(AppSettings settings, ILogger log)
    {
        if (!settings.TelemetryEnabled) return;
        if (string.IsNullOrEmpty(DefaultDsn))
        {
            log.LogInformation("Telemetry opted-in but no Sentry DSN configured at build time");
            return;
        }
        s_sentry = SentrySdk.Init(o =>
        {
            o.Dsn = DefaultDsn;
            o.Environment = "production";
            o.AutoSessionTracking = true;
            o.TracesSampleRate = 0.0;
            o.SetBeforeSend(evt =>
            {
                if (evt.Logger?.Contains("ApiKey", StringComparison.OrdinalIgnoreCase) == true) return null;
                return evt;
            });
        });
        log.LogInformation("Telemetry enabled (Sentry)");
    }

    public static void Shutdown() => s_sentry?.Dispose();
}
