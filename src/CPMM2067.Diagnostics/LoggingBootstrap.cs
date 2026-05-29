using System;
using System.IO;
using CPMM2067.Core;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace CPMM2067.Diagnostics;

public static class LoggingBootstrap
{
    private static ILoggerFactory? s_factory;
    private static InMemoryLogSink? s_inMemory;

    public static InMemoryLogSink InMemorySink => s_inMemory ?? throw new InvalidOperationException("Logging not initialized");

    public static ILoggerFactory Initialize()
    {
        if (s_factory != null) return s_factory;
        AppPaths.EnsureAll();
        s_inMemory = new InMemoryLogSink(capacity: 2_000);

        var serilog = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Async(a => a.File(
                path: Path.Combine(AppPaths.LogsDir, "cpmm2067-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}"))
            .WriteTo.Sink(s_inMemory, LogEventLevel.Debug)
            .CreateLogger();

        Log.Logger = serilog;
        s_factory = new SerilogLoggerFactory(serilog, dispose: true);
        return s_factory;
    }

    public static void Shutdown()
    {
        Log.CloseAndFlush();
        s_factory?.Dispose();
        s_factory = null;
    }
}
