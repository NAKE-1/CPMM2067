using System;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;

namespace CPMM2067.Nexus;

/// <summary>
/// Tracks Nexus's per-key API budget. Nexus emits these headers on EVERY response:
///   X-RL-Daily-Limit    — total daily budget (2500 free / 50000 premium typically)
///   X-RL-Daily-Remaining
///   X-RL-Daily-Reset    — UTC datetime when the daily counter rolls
///   X-RL-Hourly-Limit   — 100 typical (per-hour soft cap)
///   X-RL-Hourly-Remaining
///   X-RL-Hourly-Reset
/// We don't enforce anything; we just surface the numbers so the user can see when they're
/// about to be throttled. (There is no "every 5 hours" bucket; you may be thinking of the
/// hourly counter, or of the free download token TTL.)
/// </summary>
public sealed class NexusRateLimitTracker : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private int _dailyLimit, _dailyRemaining;
    private int _hourlyLimit, _hourlyRemaining;
    private DateTime? _dailyReset, _hourlyReset;
    private DateTime? _lastUpdated;
    private int _totalCallsThisSession;

    public int DailyLimit { get => _dailyLimit; private set => Set(ref _dailyLimit, value); }
    public int DailyRemaining { get => _dailyRemaining; private set => Set(ref _dailyRemaining, value); }
    public int HourlyLimit { get => _hourlyLimit; private set => Set(ref _hourlyLimit, value); }
    public int HourlyRemaining { get => _hourlyRemaining; private set => Set(ref _hourlyRemaining, value); }
    public DateTime? DailyReset { get => _dailyReset; private set => Set(ref _dailyReset, value); }
    public DateTime? HourlyReset { get => _hourlyReset; private set => Set(ref _hourlyReset, value); }
    public DateTime? LastUpdated { get => _lastUpdated; private set => Set(ref _lastUpdated, value); }
    public int TotalCallsThisSession { get => _totalCallsThisSession; private set => Set(ref _totalCallsThisSession, value); }

    public string DailyDisplay =>
        DailyLimit == 0 ? "(no API call yet)"
        : $"{DailyRemaining} / {DailyLimit} remaining" +
          (DailyReset.HasValue ? $" (resets {DailyReset.Value.ToLocalTime():yyyy-MM-dd hh:mm tt})" : "");

    public string HourlyDisplay =>
        HourlyLimit == 0 ? "(no API call yet)"
        : $"{HourlyRemaining} / {HourlyLimit} remaining" +
          (HourlyReset.HasValue ? $" (resets {HourlyReset.Value.ToLocalTime():hh:mm tt})" : "");

    public string SessionDisplay => $"{TotalCallsThisSession} call(s) this session";

    public void RecordResponse(HttpResponseHeaders headers)
    {
        if (headers == null) return;
        TotalCallsThisSession++;

        int? GetInt(string name) =>
            headers.TryGetValues(name, out var v) && int.TryParse(string.Join("", v), out var i) ? i : null;
        DateTime? GetDate(string name)
        {
            if (!headers.TryGetValues(name, out var v)) return null;
            var s = string.Join("", v);
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt) ? dt : null;
        }

        var dl = GetInt("X-RL-Daily-Limit");
        var dr = GetInt("X-RL-Daily-Remaining");
        var hl = GetInt("X-RL-Hourly-Limit");
        var hr = GetInt("X-RL-Hourly-Remaining");
        var dRes = GetDate("X-RL-Daily-Reset");
        var hRes = GetDate("X-RL-Hourly-Reset");

        if (dl.HasValue) DailyLimit = dl.Value;
        if (dr.HasValue) DailyRemaining = dr.Value;
        if (hl.HasValue) HourlyLimit = hl.Value;
        if (hr.HasValue) HourlyRemaining = hr.Value;
        if (dRes.HasValue) DailyReset = dRes.Value;
        if (hRes.HasValue) HourlyReset = hRes.Value;

        LastUpdated = DateTime.UtcNow;
        OnPropertyChanged(nameof(DailyDisplay));
        OnPropertyChanged(nameof(HourlyDisplay));
        OnPropertyChanged(nameof(SessionDisplay));
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged(string? name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? ""));
}
