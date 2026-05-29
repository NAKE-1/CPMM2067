using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CPMM2067.App.Services;

public enum GameRunState
{
    NotRunning,
    Starting,
    Running,
    Stopping,
    Crashed,
}

public sealed class GameProcessMonitor : IDisposable
{
    public const string ProcessName = "Cyberpunk2077";

    private readonly CancellationTokenSource _cts = new();
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(2);
    private GameRunState _state = GameRunState.NotRunning;
    private DateTime _startingSince = DateTime.MinValue;
    private int? _pid;

    public event Action<GameRunState, string>? Changed;
    public GameRunState State => _state;

    public void Start()
    {
        _ = Task.Run(PollLoop);
    }

    public void NotifyStarting()
    {
        _startingSince = DateTime.UtcNow;
        SetState(GameRunState.Starting, "attempting to launch…");
    }

    private async Task PollLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var procs = Process.GetProcessesByName(ProcessName);
                var running = procs.Length > 0;
                var pid = running ? procs[0].Id : (int?)null;
                foreach (var p in procs) p.Dispose();

                switch (_state)
                {
                    case GameRunState.NotRunning when running:
                    case GameRunState.Starting when running:
                        _pid = pid;
                        SetState(GameRunState.Running, $"running (pid {pid})");
                        break;

                    case GameRunState.Starting when !running:
                        if ((DateTime.UtcNow - _startingSince).TotalSeconds > 90)
                            SetState(GameRunState.Crashed, "startup gave up (no process after 90s)");
                        break;

                    case GameRunState.Running when !running:
                        SetState(GameRunState.Stopping, $"stopped (last pid {_pid})");
                        _pid = null;
                        break;

                    case GameRunState.Stopping:
                        SetState(GameRunState.NotRunning, "not running");
                        break;

                    case GameRunState.Crashed when running:
                        _pid = pid;
                        SetState(GameRunState.Running, $"running (pid {pid})");
                        break;

                    case GameRunState.NotRunning when !running:
                    case GameRunState.Running when running:
                        // steady state; no event
                        break;
                }
            }
            catch
            {
                /* swallow; polling will retry */
            }

            try { await Task.Delay(_pollInterval, _cts.Token); }
            catch { break; }
        }
    }

    private void SetState(GameRunState s, string text)
    {
        if (_state == s) return;
        _state = s;
        Changed?.Invoke(s, text);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
