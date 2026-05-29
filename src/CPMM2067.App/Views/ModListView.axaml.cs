using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CPMM2067.App.ViewModels;

namespace CPMM2067.App.Views;

public partial class ModListView : UserControl
{
    public ModListView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ModListViewModel vm) return;
        if (!e.DataTransfer.Contains(DataFormat.File)) return;

        var items = e.DataTransfer.TryGetFiles();
        if (items == null) return;

        foreach (var item in items)
        {
            if (item is not IStorageFile file) continue;
            var local = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(local)) continue;
            var ext = System.IO.Path.GetExtension(local).ToLowerInvariant();
            if (ext is not (".zip" or ".7z" or ".rar")) continue;
            await vm.InstallPathAsync(local);
        }
        e.Handled = true;
    }
}
