using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CPMM2067.App.Services;

public static class SingleInstance
{
    private const string MutexName = @"Global\cpmm2067-singleton";
    private const string PipeName = "cpmm2067-ipc";

    private static Mutex? s_mutex;

    public static bool TryAcquire()
    {
        s_mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        return created;
    }

    public static void Release()
    {
        try { s_mutex?.ReleaseMutex(); } catch { }
        s_mutex?.Dispose();
        s_mutex = null;
    }

    public static bool SendToRunningInstance(string message, int timeoutMs = 1500)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(timeoutMs);
            var bytes = Encoding.UTF8.GetBytes(message);
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void StartListener(Action<string> onMessage, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    using var ms = new MemoryStream();
                    await server.CopyToAsync(ms, ct).ConfigureAwait(false);
                    var msg = Encoding.UTF8.GetString(ms.ToArray());
                    if (!string.IsNullOrWhiteSpace(msg)) onMessage(msg);
                }
                catch (OperationCanceledException) { break; }
                catch { /* swallow and recreate */ }
            }
        }, ct);
    }
}
