using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Hardcodet.Wpf.TaskbarNotification;

namespace Simpler.Host;

// -- Tray --
public class TrayManager : IDisposable
{
    private TaskbarIcon? _tray;
    private Icon? _icon;
    private System.Windows.Controls.ContextMenu? _menu;

    public void Initialize()
    {
        _icon = CreateIcon();
        _tray = new TaskbarIcon
        {
            ToolTipText = "Simpler",
            Icon = _icon
        };
        _tray.TrayMouseDoubleClick += (_, _) => App.ShowLauncher();

        _menu = new System.Windows.Controls.ContextMenu
        {
            StaysOpen = false
        };

        var showItem = new System.Windows.Controls.MenuItem
            { Header = "Show Panel" };
        showItem.Click += (_, _) => App.ShowLauncher();

        var startupItem = new System.Windows.Controls.MenuItem
            { Header = "Launch at Startup", IsCheckable = true };
        startupItem.IsChecked = StartupManager.IsEnabled();
        startupItem.Click += (_, _) =>
        {
            StartupManager.SetEnabled(startupItem.IsChecked);
        };

        var quitItem = new System.Windows.Controls.MenuItem
            { Header = "Quit" };
        quitItem.Click += (_, _) =>
        {
            _tray?.Dispose();
            Application.Current.Shutdown();
        };

        _menu.Items.Add(showItem);
        _menu.Items.Add(startupItem);
        _menu.Items.Add(new System.Windows.Controls.Separator());
        _menu.Items.Add(quitItem);
        _menu.Placement = PlacementMode.AbsolutePoint;

        // Keep a ContextMenu assigned so WPF handles focus/close properly.
        _tray.ContextMenu = _menu;
        _tray.TrayRightMouseUp += (_, _) => ShowMenuAtCursor();
    }

    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(0x00, 0x78, 0xD4));
            g.FillEllipse(brush, 1, 1, 14, 14);
        }

        IntPtr hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        var cloned = (Icon)icon.Clone();
        DestroyIcon(hIcon);
        icon.Dispose();
        return cloned;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private void ShowMenuAtCursor()
    {
        if (_menu == null) return;
        if (!GetCursorPos(out var pt)) return;

        _menu.IsOpen = false;
        _menu.Placement = PlacementMode.AbsolutePoint;
        double scale = 96.0 / GetDpiForSystem();
        _menu.HorizontalOffset = pt.X * scale;
        _menu.VerticalOffset = pt.Y * scale;
        _menu.IsOpen = true;
    }

    public void Dispose()
    {
        _tray?.Dispose();
        _icon?.Dispose();
    }
}

// -- Global Hotkey (Win32) --
public class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private static int _idCounter = 9000;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(
        IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly ModifierKeys _mods;
    private readonly Key _key;
    private readonly Action _callback;
    private readonly int _id;
    private HwndSource? _source;
    private Window? _helperWindow;

    public GlobalHotkey(ModifierKeys mods, Key key, Action callback)
    {
        _mods     = mods;
        _key      = key;
        _callback = callback;
        _id       = System.Threading.Interlocked.Increment(ref _idCounter);
    }

    public void Register()
    {
        if (_source != null) return;

        // We need an HWND; create a hidden helper window.
        _helperWindow = new Window
        {
            Width = 0, Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Visibility = Visibility.Hidden
        };
        _helperWindow.Show();

        _source = HwndSource.FromHwnd(
            new WindowInteropHelper(_helperWindow).Handle);
        _source?.AddHook(WndProc);

        uint mods = 0;
        if (_mods.HasFlag(ModifierKeys.Alt))     mods |= 0x0001;
        if (_mods.HasFlag(ModifierKeys.Control)) mods |= 0x0002;
        if (_mods.HasFlag(ModifierKeys.Shift))   mods |= 0x0004;
        if (_mods.HasFlag(ModifierKeys.Windows)) mods |= 0x0008;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(_key);
        RegisterHotKey(_source!.Handle, _id, mods, vk);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg,
        IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && (int)wParam == _id)
        {
            _callback();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Unregister()
    {
        if (_source != null)
        {
            UnregisterHotKey(_source.Handle, _id);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }

        if (_helperWindow != null)
        {
            _helperWindow.Close();
            _helperWindow = null;
        }
    }

    public void Dispose() => Unregister();
}