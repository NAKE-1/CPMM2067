using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPMM2067.App.Services;
using CPMM2067.Conflicts;
using CPMM2067.Frameworks;

namespace CPMM2067.App.ViewModels;

public partial class ConflictsViewModel : ViewModelBase
{
    private readonly InstallJournal _journal;
    private readonly GameStateService _state;

    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _deepStatus = "(not scanned)";
    public ObservableCollection<ConflictRow> Conflicts { get; } = new();
    public ObservableCollection<DeepConflictRow> TweakConflicts { get; } = new();
    public ObservableCollection<DeepConflictRow> RedsConflicts { get; } = new();

    public ConflictsViewModel(InstallJournal journal, GameStateService state)
    {
        _journal = journal;
        _state = state;
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        Conflicts.Clear();

        // Walk every Installed entry; bucket its paths.
        var byPath = new Dictionary<string, List<(string Mod, DateTimeOffset At, string Framework)>>(
            StringComparer.OrdinalIgnoreCase);
        var installed = _journal.LoadAll()
            .Where(t => t.Entry.Status == InstallEntryStatus.Installed)
            .ToList();
        foreach (var (_, e) in installed)
        {
            foreach (var rel in e.RelativePaths)
            {
                if (!byPath.TryGetValue(rel, out var list))
                    byPath[rel] = list = new();
                list.Add((e.Name, e.At, e.Framework.ToString()));
            }
        }

        var conflicting = byPath
            .Where(kv => kv.Value.Count > 1)
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var (path, mods) in conflicting)
        {
            var names = string.Join(", ", mods.OrderBy(m => m.At).Select(m => $"{m.Mod} ({m.Framework})"));
            var winner = mods.OrderByDescending(m => m.At).First().Mod;
            Conflicts.Add(new ConflictRow(path, names, mods.Count, winner));
        }

        Status = Conflicts.Count == 0
            ? $"No conflicts across {installed.Count} installed mod(s)."
            : $"{Conflicts.Count} conflicting path(s) across {installed.Count} installed mod(s).";

        // Deep scan
        TweakConflicts.Clear();
        RedsConflicts.Clear();
        var game = _state.Current;
        if (game == null)
        {
            DeepStatus = "No game set — deep scan skipped.";
            return;
        }

        var deep = DeepConflictScanner.Scan(game);
        foreach (var c in deep.TweakXLKeyConflicts)
            TweakConflicts.Add(new DeepConflictRow(c.Key, c.Kind, c.SourceFiles.Count, string.Join("\n", c.SourceFiles)));
        foreach (var c in deep.RedscriptHookConflicts)
            RedsConflicts.Add(new DeepConflictRow(c.Key, c.Kind, c.SourceFiles.Count, string.Join("\n", c.SourceFiles)));
        DeepStatus = $"{TweakConflicts.Count} TweakXL key collision(s), {RedsConflicts.Count} redscript hook collision(s).";
    }
}

public sealed record ConflictRow(string Path, string Mods, int ModCount, string WinnerOnDisk);
public sealed record DeepConflictRow(string Key, string Kind, int FileCount, string Files);
