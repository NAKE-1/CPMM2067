using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPMM2067.App.Services;
using CPMM2067.Compat;
using CPMM2067.Core.Compat;
using CPMM2067.Core.Mods;
using CPMM2067.Frameworks;
using CPMM2067.Frameworks.RedMod;

namespace CPMM2067.App.ViewModels;

public partial class ModListViewModel : ViewModelBase
{
    private readonly GameStateService _state;
    private readonly ModScanner _scanner;
    private readonly ModInstaller _installer;
    private readonly CompatEngine _compat;

    // All scanned mods (source of truth) + filtered view for the UI.
    private readonly List<ModRow> _allMods = new();
    public ObservableCollection<ModRow> Mods { get; } = new();

    public IReadOnlyList<string> FrameworkOptions { get; } = new[]
    {
        "All", "RedMod", "Red4ext", "LegacyArchive", "ArchiveXL",
        "TweakXL", "Cet", "Redscript",
    };

    [ObservableProperty] private string _emptyMessage = "No mods scanned yet. Pick a folder on Dashboard to auto-scan.";
    [ObservableProperty] private string _scanStatus = string.Empty;
    [ObservableProperty] private string _installStatus = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedFramework = "All";

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedFrameworkChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = (SearchText ?? string.Empty).Trim();
        var fw = SelectedFramework;
        Mods.Clear();
        foreach (var row in _allMods)
        {
            if (fw != "All" && !string.Equals(row.Framework.ToString(), fw, StringComparison.OrdinalIgnoreCase))
                continue;
            if (q.Length > 0)
            {
                if (row.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                    && row.RelativePath.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                    && row.Version.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
            }
            Mods.Add(row);
        }
        ScanStatus = q.Length == 0 && fw == "All"
            ? $"Showing {Mods.Count} of {_allMods.Count} mod(s)."
            : $"Showing {Mods.Count} of {_allMods.Count} — filter: '{q}' / {fw}";
    }

    [RelayCommand]
    private void ClearFilter()
    {
        SearchText = string.Empty;
        SelectedFramework = "All";
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (_allMods.Count == 0) { ScanStatus = "Nothing to export — scan first."; return; }
        var game = _state.Current;
        var exportRoot = System.IO.Path.Combine(CPMM2067.Core.AppPaths.AppData, "exports");
        System.IO.Directory.CreateDirectory(exportRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd-hhmmsstt");
        var path = System.IO.Path.Combine(exportRoot, $"mods-{stamp}.json");

        var payload = _allMods.Select(r =>
        {
            var files = new List<string>();
            try
            {
                if (System.IO.Directory.Exists(r.AbsolutePath))
                {
                    foreach (var f in System.IO.Directory.EnumerateFiles(r.AbsolutePath, "*", System.IO.SearchOption.AllDirectories))
                        files.Add(System.IO.Path.GetRelativePath(r.AbsolutePath, f).Replace('\\', '/'));
                }
                else if (System.IO.File.Exists(r.AbsolutePath))
                {
                    files.Add(System.IO.Path.GetFileName(r.AbsolutePath));
                }
            }
            catch { /* best effort */ }

            return new
            {
                name = r.Name,
                framework = r.Framework.ToString(),
                version = r.Version,
                relativePath = r.RelativePath.Replace('\\', '/'),
                fileCount = files.Count,
                files,
            };
        }).ToList();

        var doc = new
        {
            generatedAt = DateTime.UtcNow.ToString("o"),
            cpmm2067Version = "0.1",
            gameInstall = game?.InstallDir,
            gameVersion = game?.Version.ToString(),
            modCount = payload.Count,
            mods = payload,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(doc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await System.IO.File.WriteAllTextAsync(path, json);

        ScanStatus = $"Exported {payload.Count} mod(s) → {path}";

        var w = MainWindowAccessor.Get();
        if (w != null)
        {
            await Views.ConfirmDialog.ShowResultAsync(
                w,
                title: "CPMM2067 — export complete",
                headline: $"[ ✓ EXPORTED {payload.Count} MOD(S) ]",
                body: $"Saved to:\n{path}\n\nOne JSON object per mod, with name / framework / version / relative path / full file list (recursive).");
        }
    }

    public bool TestingMode => AppHost.Settings.TestingMode;
    public string TestingModeBanner => TestingMode
        ? "[ TESTING MODE — installs are dry-run; no files written ]"
        : string.Empty;

    public ModListViewModel(GameStateService state, ModScanner scanner, ModInstaller installer, CompatEngine compat)
    {
        _state = state;
        _scanner = scanner;
        _installer = installer;
        _compat = compat;
        _state.Changed += OnGameChanged;

        if (_state.Current != null && AppHost.Settings.AutoScanOnStartup)
            _ = ScanAsync();
    }

    private void OnGameChanged(Core.Game.GameInstallation? game)
    {
        if (game == null) return;
        if (!AppHost.Settings.AutoScanOnStartup) return;
        Dispatcher.UIThread.Post(() => _ = ScanAsync());
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        var game = _state.Current;
        if (game == null) { ScanStatus = "No game set. Use Dashboard › Pick folder first."; return; }
        ScanStatus = $"Scanning {game.InstallDir}…";
        _allMods.Clear();
        Mods.Clear();
        var found = await _scanner.ScanAsync(game);
        foreach (var m in found)
            _allMods.Add(new ModRow(this, m.Name, m.Version ?? "—", m.Framework, ModEnabled.Enabled, m.RelativePath, m.AbsolutePath));
        ApplyFilter();
        ScanStatus = $"Found {found.Count} mod(s).";
        if (found.Count == 0)
            EmptyMessage = "Scan ran but no mods were found in the configured install.";

        // Kick off compat evaluation per row (non-blocking)
        _ = Task.Run(async () =>
        {
            foreach (var row in _allMods.ToArray())
            {
                try
                {
                    var manifest = new ModManifest
                    {
                        Id = ModId.NewId(),
                        Name = row.Name,
                        Version = row.Version == "—" ? "0.0.0" : row.Version,
                        Framework = row.Framework,
                        SupportedGameVersion = null,
                    };
                    var verdict = await _compat.EvaluateAsync(manifest, game);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => row.ApplyVerdict(verdict));
                }
                catch { /* leave row's "?" placeholder */ }
            }
        });
    }

    [RelayCommand]
    private async Task InstallFromPickerAsync()
    {
        var game = _state.Current;
        if (game == null) { InstallStatus = "No game set."; return; }
        var window = MainWindow();
        if (window?.StorageProvider == null) return;
        var picked = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pick a mod archive (.zip)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Mod archives") { Patterns = new[] { "*.zip" } },
                new FilePickerFileType("Any") { Patterns = new[] { "*" } },
            },
        });
        if (picked.Count == 0) return;
        await InstallPathAsync(picked[0].Path.LocalPath);
    }

    public async Task InstallPathAsync(string path)
    {
        var game = _state.Current;
        if (game == null) { InstallStatus = "No game set."; return; }
        var dry = AppHost.Settings.TestingMode;
        InstallStatus = dry ? $"[ DRY RUN ] Inspecting {Path.GetFileName(path)}…" : $"Installing {Path.GetFileName(path)}…";
        var result = await _installer.InstallFromArchiveAsync(path, game, dryRun: dry);
        InstallStatus = result.Message;
        if (result.Ok && !dry) await ScanAsync();
    }

    public async Task DeleteAsync(ModRow row)
    {
        var game = _state.Current;
        if (game == null) { ScanStatus = "No game set."; return; }
        try
        {
            if (Directory.Exists(row.AbsolutePath))
                Directory.Delete(row.AbsolutePath, recursive: true);
            else if (File.Exists(row.AbsolutePath))
                File.Delete(row.AbsolutePath);

            if (row.Framework == ModFramework.RedMod)
            {
                var folder = Path.GetFileName(row.AbsolutePath);
                if (File.Exists(game.ModsJsonPath))
                {
                    try
                    {
                        var json = File.ReadAllText(game.ModsJsonPath);
                        var doc = System.Text.Json.JsonSerializer.Deserialize<RedModsJson>(json) ?? new RedModsJson();
                        doc.Mods.RemoveAll(m =>
                            string.Equals(m.Folder, folder, StringComparison.OrdinalIgnoreCase));
                        File.WriteAllText(game.ModsJsonPath,
                            System.Text.Json.JsonSerializer.Serialize(doc,
                                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    }
                    catch { }
                }
            }

            _allMods.Remove(row);
            Mods.Remove(row);
            ScanStatus = $"Deleted {row.Name}. ({_allMods.Count} remaining)";
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ScanStatus = $"Delete failed: {ex.Message}";
        }
    }

    private static Window? MainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}

public sealed partial class ModRow : ObservableObject
{
    private readonly ModListViewModel _parent;
    public string Name { get; }
    public string Version { get; }
    public ModFramework Framework { get; }
    public ModEnabled State { get; }
    public string RelativePath { get; }
    public string AbsolutePath { get; }

    [ObservableProperty] private bool _confirmingDelete;
    [ObservableProperty] private string _compatLabel = "?";
    [ObservableProperty] private string _compatReasons = "Not evaluated yet";
    [ObservableProperty] private Avalonia.Media.IBrush _compatBrush = Avalonia.Media.Brushes.Gray;

    public ModRow(ModListViewModel parent, string name, string version, ModFramework framework,
        ModEnabled state, string relativePath, string absolutePath)
    {
        _parent = parent;
        Name = name; Version = version; Framework = framework; State = state;
        RelativePath = relativePath; AbsolutePath = absolutePath;
    }

    [RelayCommand] private void RequestDelete() => ConfirmingDelete = true;
    [RelayCommand] private void CancelDelete() => ConfirmingDelete = false;
    [RelayCommand] private async Task ConfirmDeleteAsync()
    {
        ConfirmingDelete = false;
        await _parent.DeleteAsync(this);
    }

    public void ApplyVerdict(CompatVerdict v)
    {
        var app = Avalonia.Application.Current;
        Avalonia.Media.IBrush BrushFor(string key) =>
            app != null && app.Resources.TryGetResource(key, app.ActualThemeVariant, out var b) && b is Avalonia.Media.IBrush ib
                ? ib : Avalonia.Media.Brushes.Gray;

        (CompatLabel, CompatBrush) = v.Status switch
        {
            CompatStatus.Compatible   => ("OK",    BrushFor("OnlineBrush")),
            CompatStatus.Risky        => ("RISKY", BrushFor("IdleBrush")),
            CompatStatus.Incompatible => ("BAD",   BrushFor("DndBrush")),
            _                          => ("?",   BrushFor("Text5Brush")),
        };
        CompatReasons = string.Join("\n• ", new[] { v.Headline }.Concat(v.Reasons));
    }
}
