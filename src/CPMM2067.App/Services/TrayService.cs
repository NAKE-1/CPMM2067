using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace CPMM2067.App.Services;

public sealed class TrayService : IDisposable
{
    private TrayIcon? _icon;

    public void Install()
    {
        if (_icon != null) return;

        var showItem = new NativeMenuItem("Show CPMM2067");
        showItem.Click += (_, __) => Show();

        var hideItem = new NativeMenuItem("Hide to tray");
        hideItem.Click += (_, __) => Hide();

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, __) => Quit();

        var menu = new NativeMenu();
        menu.Add(showItem);
        menu.Add(hideItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(quitItem);

        _icon = new TrayIcon
        {
            ToolTipText = "CPMM2067 — Cyberpunk 2077 mod manager",
            IsVisible = true,
            Menu = menu,
        };
        _icon.Clicked += (_, __) => Show();

        // Register so Avalonia paints it
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _icon });
    }

    public void Show()
    {
        var w = MainWindowAccessor.Get();
        if (w == null) return;
        if (!w.IsVisible) w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        w.Topmost = true;
        w.Topmost = false;
    }

    public void Hide()
    {
        var w = MainWindowAccessor.Get();
        w?.Hide();
    }

    public void Quit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            d.Shutdown();
    }

    public void Dispose()
    {
        if (_icon != null)
        {
            _icon.IsVisible = false;
            _icon = null;
        }
    }
}
