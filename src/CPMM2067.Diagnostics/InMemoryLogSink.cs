using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace CPMM2067.Diagnostics;

public sealed record LogLine(DateTimeOffset Timestamp, LogEventLevel Level, string Source, string Message, string? Exception);

public sealed class InMemoryLogSink : ILogEventSink
{
    private readonly int _capacity;
    private readonly ConcurrentQueue<LogLine> _queue = new();
    private readonly MessageTemplateTextFormatter _formatter =
        new("{Message:lj}", null);

    public event Action<LogLine>? OnEmit;

    public InMemoryLogSink(int capacity) => _capacity = capacity;

    public void Emit(LogEvent logEvent)
    {
        using var sw = new StringWriter();
        _formatter.Format(logEvent, sw);
        var source = logEvent.Properties.TryGetValue("SourceContext", out var sc)
            ? sc.ToString().Trim('"') : string.Empty;

        var line = new LogLine(
            logEvent.Timestamp,
            logEvent.Level,
            source,
            sw.ToString(),
            logEvent.Exception?.ToString());

        _queue.Enqueue(line);
        while (_queue.Count > _capacity && _queue.TryDequeue(out _)) { }
        OnEmit?.Invoke(line);
    }

    public IReadOnlyList<LogLine> Snapshot() => _queue.ToArray();
}
