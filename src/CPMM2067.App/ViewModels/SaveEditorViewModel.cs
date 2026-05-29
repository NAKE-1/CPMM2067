using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPMM2067.App.Services;
using CPMM2067.Core;

namespace CPMM2067.App.ViewModels;

public partial class SaveEditorViewModel : ViewModelBase
{
    [ObservableProperty] private string _heading = "Save editor — external launcher (CyberCAT-SimpleGUI)";

    [ObservableProperty] private string _details =
        "CPMM2067 doesn't ship its own save-format parser. The Cyberpunk save format (.dat / CR2W " +
        "binary) is partially reverse-engineered; the community tool CyberCAT-SimpleGUI is the " +
        "established choice for editing inventory / perks / attributes / money.\n\n" +
        "Configure the path to the CyberCAT-SimpleGUI exe below — we'll launch it pointing at " +
        "whichever save you pick. Per-save [ open in CyberCAT ] buttons are also on the Saves page.";

    [ObservableProperty] private string _saveEditorExe = string.Empty;
    [ObservableProperty] private string _status = string.Empty;

    public SaveEditorViewModel()
    {
        _saveEditorExe = AppHost.Settings.SaveEditorExe ?? string.Empty;
    }

    partial void OnSaveEditorExeChanged(string value)
    {
        var s = AppHost.Settings with { SaveEditorExe = string.IsNullOrWhiteSpace(value) ? null : value.Trim() };
        AppHost.UpdateSettings(s);
    }

    [RelayCommand]
    private void AutoDetect()
    {
        var found = SaveEditorLauncher.AutoDetect();
        if (string.IsNullOrEmpty(found))
        {
            Status = "Could not find CyberCAT-SimpleGUI in common locations. Click [ download ] or [ pick exe… ].";
            return;
        }
        SaveEditorExe = found;
        Status = $"Found: {found}";
    }

    [RelayCommand]
    private async Task PickExeAsync()
    {
        var window = MainWindowAccessor.Get();
        if (window?.StorageProvider == null) return;
        var picked = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pick CP2077SaveEditor.exe",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Programs") { Patterns = new[] { "*.exe" } },
            },
        });
        if (picked.Count > 0) SaveEditorExe = picked[0].Path.LocalPath;
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        Status = "Starting CyberCAT-SimpleGUI download…";
        try
        {
            var result = await CyberCatInstaller.DownloadAndInstallAsync(
                progress: msg => Avalonia.Threading.Dispatcher.UIThread.Post(() => Status = msg));
            if (result.Ok && !string.IsNullOrEmpty(result.ExePath))
            {
                SaveEditorExe = result.ExePath;
                Status = result.Message;
            }
            else
            {
                Status = "Auto-download failed: " + result.Message +
                         "  —  Opening the Nexus page as fallback.";
                BrowserLauncher.OpenUrl(SaveEditorLauncher.DefaultNexusUrl);
            }
        }
        catch (Exception ex)
        {
            Status = "Auto-download error: " + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenNexusPage()
        => BrowserLauncher.OpenUrl(SaveEditorLauncher.DefaultNexusUrl);

    [RelayCommand]
    private async Task PickSaveAndOpenAsync()
    {
        var window = MainWindowAccessor.Get();
        if (window?.StorageProvider == null) return;
        var startFolder = AppPaths.SavesFolder;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pick a save folder (or any folder containing sav.dat)",
            AllowMultiple = false,
            SuggestedStartLocation = Directory.Exists(startFolder)
                ? await window.StorageProvider.TryGetFolderFromPathAsync(startFolder)
                : null,
        });
        if (folders.Count == 0) return;
        if (SaveEditorLauncher.TryLaunch(folders[0].Path.LocalPath, out var error))
            Status = "Launched CyberCAT for " + folders[0].Path.LocalPath;
        else
            Status = error;
    }
}
