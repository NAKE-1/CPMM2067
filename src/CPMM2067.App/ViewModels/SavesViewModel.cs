using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPMM2067.App.Services;
using CPMM2067.Core;
using CPMM2067.Saves;

namespace CPMM2067.App.ViewModels;

public partial class SavesViewModel : ViewModelBase
{
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _savesPath = AppPaths.SavesFolder;
    [ObservableProperty] private string _backupRoot = DefaultBackupRoot;

    public ObservableCollection<SaveRow> Saves { get; } = new();

    public static string DefaultBackupRoot => Path.Combine(AppContext.BaseDirectory, "backups", "saves");

    public SavesViewModel() => Refresh();

    [RelayCommand]
    private void Refresh()
    {
        Saves.Clear();
        if (!Directory.Exists(AppPaths.SavesFolder))
        {
            Status = $"Saves dir not found: {AppPaths.SavesFolder}";
            return;
        }
        var dirs = new DirectoryInfo(AppPaths.SavesFolder)
            .EnumerateDirectories()
            .OrderByDescending(d => d.LastWriteTime)
            .ToList();
        foreach (var d in dirs) Saves.Add(new SaveRow(this, d));
        Status = $"{Saves.Count} save(s).";
    }

    [RelayCommand]
    private void OpenSavesFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.SavesFolder) { UseShellExecute = true }); }
        catch { }
    }

    [RelayCommand]
    private void OpenDefaultBackupRoot()
    {
        Directory.CreateDirectory(DefaultBackupRoot);
        try { Process.Start(new ProcessStartInfo("explorer.exe", DefaultBackupRoot) { UseShellExecute = true }); }
        catch { }
    }

    public async Task DeleteAsync(SaveRow row)
    {
        var w = MainWindowAccessor.Get();
        if (w != null)
        {
            var r = await Views.ConfirmDialog.ShowAsync(
                w,
                title: "CPMM2067 — delete save?",
                headline: $"[ DELETE // {row.Name} ]",
                body: $"This will permanently remove:\n{row.Path}\n\n" +
                      "A backup is taken first into the backups folder. " +
                      "You can also click [ BACKUP ] separately to confirm before deleting.",
                primaryLabel: "[ BACKUP + DELETE ]",
                secondaryLabel: "[ DELETE WITHOUT BACKUP ]");
            if (r == Views.ConfirmResult.Cancel) { Status = "Delete cancelled."; return; }
            if (r == Views.ConfirmResult.Primary) await BackupAsync(row);
        }
        try
        {
            Directory.Delete(row.Path, recursive: true);
            Status = $"Deleted {row.Name}.";
            Refresh();
        }
        catch (Exception ex)
        {
            Status = $"Delete failed: {ex.Message}";
        }
    }

    public async Task DuplicateAsync(SaveRow row)
    {
        try
        {
            var baseName = row.Name;
            var parent = Directory.GetParent(row.Path)?.FullName ?? AppPaths.SavesFolder;
            // CP2077 save folders use sequence-suffixed names; just append "-copy" + stamp
            var dst = Path.Combine(parent, $"{baseName}-copy-{DateTime.Now:HHmmss}");
            await Task.Run(() => CopyDir(row.Path, dst));
            Status = $"Duplicated {row.Name} → {Path.GetFileName(dst)}.";
            Refresh();
        }
        catch (Exception ex)
        {
            Status = $"Duplicate failed: {ex.Message}";
        }
    }

    public async Task RenameAsync(SaveRow row, string newName)
    {
        try
        {
            // Rename the visible name inside metadata*.json (CP2077 reads this) and the folder itself
            var trimmed = newName.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed == row.Name) return;

            foreach (var meta in Directory.EnumerateFiles(row.Path, "metadata*.json"))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(meta);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var dict = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>>(json);
                    if (dict == null) continue;
                    dict["name"] = System.Text.Json.JsonSerializer.SerializeToElement(trimmed);
                    var serialized = System.Text.Json.JsonSerializer.Serialize(dict,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(meta, serialized);
                }
                catch { /* skip unreadable metadata files */ }
            }

            // Rename folder — make file-system-safe
            var safe = string.Concat(trimmed.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var parent = Directory.GetParent(row.Path)?.FullName ?? AppPaths.SavesFolder;
            var newPath = Path.Combine(parent, safe);
            if (!string.Equals(newPath, row.Path, StringComparison.OrdinalIgnoreCase) && !Directory.Exists(newPath))
            {
                Directory.Move(row.Path, newPath);
            }

            Status = $"Renamed to {trimmed}.";
            Refresh();
        }
        catch (Exception ex)
        {
            Status = $"Rename failed: {ex.Message}";
        }
    }

    public async Task BackupAsync(SaveRow row)
    {
        Directory.CreateDirectory(DefaultBackupRoot);
        var dst = Path.Combine(DefaultBackupRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{row.Name}");
        Status = $"Copying {row.Name} → {dst}…";
        try
        {
            await Task.Run(() => CopyDir(row.Path, dst));
            Status = $"Backed up {row.Name} → {dst}";

            var w = MainWindowAccessor.Get();
            if (w != null)
            {
                await Views.ConfirmDialog.ShowResultAsync(
                    w,
                    title: "CPMM2067 — save backup complete",
                    headline: $"[ ✓ BACKED UP // {row.Name} ]",
                    body: $"Source : {row.Path}\nTarget : {dst}\n\nSafe to revert by copying the folder back into the saves directory.");
            }
        }
        catch (Exception ex)
        {
            Status = $"Backup failed: {ex.Message}";
        }
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.EnumerateDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}

public sealed partial class SaveRow : ObservableObject
{
    private readonly SavesViewModel _parent;
    public string Name { get; }
    public string Path { get; }
    public string LastModified { get; }
    public Bitmap? Thumbnail { get; }

    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private string _renameBuffer = "";

    public SaveRow(SavesViewModel parent, DirectoryInfo dir)
    {
        _parent = parent;
        Name = dir.Name;
        Path = dir.FullName;
        LastModified = dir.LastWriteTime.ToString("yyyy-MM-dd hh:mm tt");
        _renameBuffer = dir.Name;

        var pngPath = System.IO.Path.Combine(dir.FullName, "screenshot.png");
        if (!File.Exists(pngPath))
            pngPath = Directory.EnumerateFiles(dir.FullName, "*.png").FirstOrDefault() ?? "";
        if (File.Exists(pngPath))
        {
            try { Thumbnail = new Bitmap(pngPath); }
            catch { Thumbnail = null; }
        }
    }

    [RelayCommand] private async Task BackupAsync() => await _parent.BackupAsync(this);

    [RelayCommand]
    private void OpenFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", Path) { UseShellExecute = true }); }
        catch { }
    }

    [RelayCommand] private void StartRename() => IsRenaming = true;
    [RelayCommand] private void CancelRename() { RenameBuffer = Name; IsRenaming = false; }
    [RelayCommand] private async Task ConfirmRenameAsync()
    {
        IsRenaming = false;
        await _parent.RenameAsync(this, RenameBuffer);
    }
    [RelayCommand] private async Task DeleteAsync() => await _parent.DeleteAsync(this);
    [RelayCommand] private async Task DuplicateAsync() => await _parent.DuplicateAsync(this);

    [RelayCommand]
    private void EditInCyberCat()
    {
        if (CPMM2067.App.Services.SaveEditorLauncher.TryLaunch(Path, out var err))
        {
            // success — leave Status alone, child window handles UX
        }
        else
        {
            // surface to the parent VM's Status field
            _parent.GetType().GetProperty("Status")?.SetValue(_parent, err);
        }
    }

    [RelayCommand]
    private async Task InspectAsync()
    {
        SaveScanResult result;
        try { result = await Task.Run(() => SaveModInspector.Inspect(Path)); }
        catch (Exception ex)
        {
            var win = MainWindowAccessor.Get();
            if (win != null)
                await Views.ConfirmDialog.ShowResultAsync(win,
                    "CPMM2067 — inspect failed",
                    $"[ ✗ INSPECT FAILED // {Name} ]",
                    ex.Message);
            return;
        }

        var window = MainWindowAccessor.Get();
        if (window == null) return;
        await Views.ConfirmDialog.ShowResultAsync(
            window,
            title: $"CPMM2067 — mod fingerprint of {Name}",
            headline: $"[ INSPECT // {Name} :: {result.ModLikelyIds.Count} mod-likely ID(s) ]",
            body: result.ToReport());
    }
}
