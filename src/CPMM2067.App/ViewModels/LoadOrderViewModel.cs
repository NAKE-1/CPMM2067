using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPMM2067.App.Services;
using CPMM2067.Core.Game;
using CPMM2067.Frameworks.RedMod;

namespace CPMM2067.App.ViewModels;

public partial class LoadOrderViewModel : ViewModelBase
{
    private readonly GameStateService _state;
    private static readonly JsonSerializerOptions s_opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ObservableCollection<RedModRow> RedMod { get; } = new();

    [ObservableProperty] private string _redmodStatus = "(not loaded)";
    [ObservableProperty] private bool _hasUnsavedChanges;
    [ObservableProperty] private string _legacyArchiveTabMessage =
        "Legacy .archive load-order arrives in v1.1 (file rename to NN_prefix).";
    [ObservableProperty] private string _red4extTabMessage =
        "RED4ext plugins have no load order; the game discovers them in directory order.";

    public LoadOrderViewModel(GameStateService state)
    {
        _state = state;
        _state.Changed += _ => Refresh();
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        RedMod.Clear();
        HasUnsavedChanges = false;
        var game = _state.Current;
        if (game == null) { RedmodStatus = "No game set — pick a folder on Dashboard first."; return; }

        // Source 1: mods.json (if present, it carries explicit order + enabled flags)
        var modsJsonPath = game.ModsJsonPath;
        if (File.Exists(modsJsonPath))
        {
            try
            {
                var doc = JsonSerializer.Deserialize<RedModsJson>(File.ReadAllText(modsJsonPath), s_opts)
                          ?? new RedModsJson();
                for (var i = 0; i < doc.Mods.Count; i++)
                {
                    var m = doc.Mods[i];
                    RedMod.Add(new RedModRow(this, i + 1, m.Folder, m.Enabled));
                }
            }
            catch (Exception ex) { RedmodStatus = $"mods.json parse failed: {ex.Message}"; return; }
        }

        // Source 2: any mod folders under mods/ that aren't in mods.json yet
        if (Directory.Exists(game.ModsDir))
        {
            var have = new HashSet<string>(RedMod.Select(r => r.Folder), StringComparer.OrdinalIgnoreCase);
            foreach (var dir in Directory.EnumerateDirectories(game.ModsDir).Select(Path.GetFileName))
            {
                if (dir == null) continue;
                if (have.Add(dir))
                    RedMod.Add(new RedModRow(this, RedMod.Count + 1, dir, enabled: true));
            }
        }

        RenumberRows();
        RedmodStatus = $"{RedMod.Count} REDmod folder(s).";
    }

    public void MoveUp(RedModRow row)
    {
        var i = RedMod.IndexOf(row);
        if (i <= 0) return;
        RedMod.Move(i, i - 1);
        RenumberRows();
        HasUnsavedChanges = true;
    }

    public void MoveDown(RedModRow row)
    {
        var i = RedMod.IndexOf(row);
        if (i < 0 || i >= RedMod.Count - 1) return;
        RedMod.Move(i, i + 1);
        RenumberRows();
        HasUnsavedChanges = true;
    }

    public void Toggle(RedModRow row)
    {
        row.Enabled = !row.Enabled;
        HasUnsavedChanges = true;
    }

    public void MarkDirty() => HasUnsavedChanges = true;

    private void RenumberRows()
    {
        for (var i = 0; i < RedMod.Count; i++) RedMod[i].Index = i + 1;
    }

    [RelayCommand]
    private async Task SaveOrderAsync()
    {
        var game = _state.Current;
        if (game == null) { RedmodStatus = "No game set."; return; }

        var doc = new RedModsJson
        {
            Mods = RedMod.Select(r => new RedModsJsonEntry { Folder = r.Folder, Enabled = r.Enabled }).ToList(),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(game.ModsJsonPath)!);
        await File.WriteAllTextAsync(game.ModsJsonPath, JsonSerializer.Serialize(doc, s_opts));
        HasUnsavedChanges = false;
        RedmodStatus = $"Saved {RedMod.Count} entries → {game.ModsJsonPath}";

        var w = MainWindowAccessor.Get();
        if (w != null)
        {
            await Views.ConfirmDialog.ShowResultAsync(
                w,
                title: "CPMM2067 — load order saved",
                headline: "[ ✓ MODS.JSON UPDATED ]",
                body: $"Wrote {RedMod.Count} REDmod entries to:\n{game.ModsJsonPath}\n\nNext [ DEPLOY + PLAY ] applies the new order via redMod.exe.");
        }
    }
}

public sealed partial class RedModRow : ObservableObject
{
    private readonly LoadOrderViewModel _parent;
    public string Folder { get; }

    [ObservableProperty] private int _index;
    [ObservableProperty] private bool _enabled;

    public RedModRow(LoadOrderViewModel parent, int index, string folder, bool enabled)
    {
        _parent = parent;
        _index = index;
        Folder = folder;
        _enabled = enabled;
    }

    [RelayCommand] private void MoveUp() => _parent.MoveUp(this);
    [RelayCommand] private void MoveDown() => _parent.MoveDown(this);

    partial void OnEnabledChanged(bool value) => _parent.MarkDirty();
}
