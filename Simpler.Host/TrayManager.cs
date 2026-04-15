using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Simpler.Host;

// -- Tray --
public class TrayManager : IDisposable
{
    private Forms.NotifyIcon? _tray;
    private Icon? _icon;
    private Forms.ContextMenuStrip? _menu;

    public void Initialize()
    {
        _icon = CreateIcon();
        _menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = true,
            ShowCheckMargin = true,
            Font = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point),
            BackColor = Color.FromArgb(42, 42, 42),
            ForeColor = Color.FromArgb(235, 235, 235),
            Padding = new Forms.Padding(4)
        };
        _menu.Renderer = new SimplerMenuRenderer();

        var showItem = new Forms.ToolStripMenuItem("Show Panel");
        showItem.ForeColor = _menu.ForeColor;
        showItem.Padding = new Forms.Padding(12, 8, 12, 8);
        showItem.Click += (_, _) => App.ShowLauncher();

        var startupItem = new Forms.ToolStripMenuItem("Launch at Startup")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled()
        };
        startupItem.ForeColor = _menu.ForeColor;
        startupItem.Padding = new Forms.Padding(12, 8, 12, 8);
        startupItem.Click += (_, _) =>
        {
            StartupManager.SetEnabled(startupItem.Checked);
        };

        var quitItem = new Forms.ToolStripMenuItem("Quit");
        quitItem.ForeColor = _menu.ForeColor;
        quitItem.Padding = new Forms.Padding(12, 8, 12, 8);
        quitItem.Click += (_, _) =>
        {
            if (Application.Current != null)
                Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
        };

        var separator = new Forms.ToolStripSeparator
        {
            Margin = new Forms.Padding(10, 4, 10, 4)
        };

        _menu.Items.Add(showItem);
        _menu.Items.Add(startupItem);
        _menu.Items.Add(separator);
        _menu.Items.Add(quitItem);
        _menu.Opening += (_, _) => startupItem.Checked = StartupManager.IsEnabled();

        _tray = new Forms.NotifyIcon
        {
            Text = "Simpler",
            Icon = _icon,
            ContextMenuStrip = _menu,
            Visible = true
        };
        _tray.MouseDoubleClick += (_, _) => App.ShowLauncher();
    }

    public void Dispose()
    {
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        _menu?.Dispose();
        _menu = null;
        _icon?.Dispose();
        _icon = null;
    }

    private sealed class SimplerMenuColorTable : Forms.ProfessionalColorTable
    {
        private static readonly Color Bg = Color.FromArgb(42, 42, 42);
        private static readonly Color Hover = Color.FromArgb(58, 58, 58);
        private static readonly Color Border = Color.FromArgb(82, 82, 82);
        private static readonly Color Separator = Color.FromArgb(66, 66, 66);

        public override Color ToolStripDropDownBackground => Bg;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Bg;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientMiddle => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color SeparatorDark => Separator;
        public override Color SeparatorLight => Separator;
        public override Color CheckBackground => Hover;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
    }

    private sealed class SimplerMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        private static readonly Color Bg = Color.FromArgb(42, 42, 42);
        private static readonly Color Hover = Color.FromArgb(58, 58, 58);
        private static readonly Color Separator = Color.FromArgb(66, 66, 66);
        private static readonly Color CheckColor = Color.FromArgb(214, 221, 228);

        public SimplerMenuRenderer() : base(new SimplerMenuColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            var rect = new Rectangle(System.Drawing.Point.Empty, e.Item.Size);
            using var brush = new SolidBrush(e.Item.Selected ? Hover : Bg);
            e.Graphics.FillRectangle(brush, rect);
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            int x1 = 14;
            int x2 = e.Item.Width - 14;
            int y = e.Item.ContentRectangle.Top + (e.Item.ContentRectangle.Height / 2);
            using var pen = new Pen(Separator);
            e.Graphics.DrawLine(pen, x1, y, x2, y);
        }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            if (e.Item is not Forms.ToolStripMenuItem menuItem || !menuItem.Checked)
            {
                base.OnRenderItemCheck(e);
                return;
            }

            var r = e.ImageRectangle;
            int cx = r.Left + (r.Width / 2);
            int cy = r.Top + (r.Height / 2);

            using var pen = new Pen(CheckColor, 2.8f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, new[]
            {
                new System.Drawing.Point(cx - 6, cy),
                new System.Drawing.Point(cx - 1, cy + 5),
                new System.Drawing.Point(cx + 7, cy - 5)
            });
        }
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
}

// -- Global Hotkey (Win32) --
public class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private static int _idCounter = 9000;

    [DllImport("user32.dll", SetLastError = true)]
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
    private bool _isRegistered;
    private int _lastError;

    public bool IsRegistered => _isRegistered;
    public int LastError => _lastError;

    public GlobalHotkey(ModifierKeys mods, Key key, Action callback)
    {
        _mods     = mods;
        _key      = key;
        _callback = callback;
        _id       = System.Threading.Interlocked.Increment(ref _idCounter);
    }

    public bool Register()
    {
        if (_source != null) return _isRegistered;

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
        bool ok = RegisterHotKey(_source!.Handle, _id, mods, vk);
        if (!ok)
        {
            _lastError = Marshal.GetLastWin32Error();
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
            _helperWindow.Close();
            _helperWindow = null;
            _isRegistered = false;
            return false;
        }

        _isRegistered = true;
        return true;
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
