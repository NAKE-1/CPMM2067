using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPMM2067.App.Services;
using CPMM2067.Diagnostics;
using CPMM2067.Frameworks;

namespace CPMM2067.App.ViewModels;

public partial class LoadReportViewModel : ViewModelBase
{
    private readonly GameStateService _state;
    private readonly ModScanner _scanner;

    [ObservableProperty] private string _status = "(not loaded)";
    [ObservableProperty] private int _loadedCount;
    [ObservableProperty] private int _staleCount;
    [ObservableProperty] private int _disabledCount;

    public ObservableCollection<LoadReportRow> Rows { get; } = new();

    public LoadReportViewModel(GameStateService state, ModScanner scanner)
    {
        _state = state;
        _scanner = scanner;
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Rows.Clear();
        LoadedCount = 0; DisabledCount = 0; StaleCount = 0;
        var game = _state.Current;
        if (game == null) { Status = "No game set."; return; }

        Status = "Parsing loader logs + scanning disk…";

        var loaded = await Task.Run(() => LoadedModParser.ParseAll(game));
        var onDisk = await _scanner.ScanAsync(game);

        // Build a set of all known names across both sides, then classify each.
        var loadedByName = loaded
            .GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var diskByName = onDisk
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var allNames = new System.Collections.Generic.SortedSet<string>(
            loadedByName.Keys.Concat(diskByName.Keys), StringComparer.OrdinalIgnoreCase);

        foreach (var name in allNames)
        {
            loadedByName.TryGetValue(name, out var l);
            diskByName.TryGetValue(name, out var d);

            string verdict;
            string brush;
            string detail;

            if (d != null && l != null)
            {
                if (l.Status == LoadStatus.Failed)
                {
                    verdict = "LOAD FAIL"; brush = "DndBrush";
                    detail = l.Error ?? "loader reported failure";
                    DisabledCount++;
                }
                else
                {
                    verdict = "LOADED"; brush = "OnlineBrush";
                    detail = "present on disk and confirmed in loader log";
                    LoadedCount++;
                }
            }
            else if (d != null && l == null)
            {
                verdict = "ON DISK ONLY"; brush = "IdleBrush";
                detail = "present in the install but no matching entry in any loader log — game has not loaded it (deps missing? loader crashed? mismatch with patch?)";
                DisabledCount++;
            }
            else
            {
                verdict = "STALE LOG"; brush = "Text5Brush";
                detail = "loader log mentions this name but no matching file/folder on disk";
                StaleCount++;
            }

            Rows.Add(new LoadReportRow(
                name,
                l?.Version ?? d?.Version,
                d?.Framework.ToString() ?? l?.Source.ToString() ?? "?",
                verdict, brush, detail,
                BrushResolver.ResolveBrush(brush)));
        }

        Status = $"{LoadedCount} loaded, {DisabledCount} on-disk-not-loaded, {StaleCount} stale";
    }
}

public sealed record LoadReportRow(
    string Name,
    string? Version,
    string Source,
    string Verdict,
    string BrushKey,
    string Detail,
    Avalonia.Media.IBrush PillBrush);

internal static class BrushResolver
{
    public static Avalonia.Media.IBrush ResolveBrush(string key)
    {
        var app = Avalonia.Application.Current;
        if (app != null && app.Resources.TryGetResource(key, app.ActualThemeVariant, out var b)
            && b is Avalonia.Media.IBrush ib) return ib;
        return Avalonia.Media.Brushes.Gray;
    }
}
