using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Simpler.Core;
using Simpler.Core.Context;
using Simpler.Core.Models;

namespace Simpler.Host;

public partial class LauncherWindow : Window
{
    private List<ScriptMeta> _scripts = new();
    private readonly RunnerRegistry _registry = App.Registry;
    private readonly string _scriptsDir;
    private DispatcherTimer? _focusTimer;
    private IntPtr _hwnd;
    private DateTime _openedAt;

    private static ScriptHotkeyService? _sharedHotkeyService;
    private readonly ScriptHotkeyService _hotkeyService;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public LauncherWindow()
    {
        InitializeComponent();
        _scriptsDir = ResolveScriptsDir();
        _sharedHotkeyService ??= new ScriptHotkeyService(_scriptsDir, ShowNotification);
        _hotkeyService = _sharedHotkeyService;
        Loaded += (_, _) => OnLoaded();
        Closed += (_, _) => StopFocusTimer();
    }

    private static string ResolveScriptsDir()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);
            string? fallback = null;

            for (int i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(dir.FullName, "scripts");
                if (Directory.Exists(candidate) &&
                    Directory.GetFiles(candidate, "*.json").Length > 0)
                {
                    fallback = candidate;

                    // Prefer repo root if we can detect it.
                    var sln = Path.Combine(dir.FullName, "Simpler.sln");
                    var git = Path.Combine(dir.FullName, ".git");
                    if (File.Exists(sln) || Directory.Exists(git))
                        return candidate;
                }

                dir = dir.Parent;
            }

            if (!string.IsNullOrWhiteSpace(fallback))
                return fallback;
        }
        catch { }

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts");
    }
    private void OnLoaded()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _openedAt = DateTime.Now;
        StartFocusTimer();
        RefreshScripts();
        SearchBox.Focus();
    }

    private void StartFocusTimer()
    {
        _focusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _focusTimer.Tick += (_, _) =>
        {
            // Grace period right after open to avoid instant close
            if ((DateTime.Now - _openedAt).TotalMilliseconds < 500)
                return;

            IntPtr fg = GetForegroundWindow();
            if (fg != _hwnd)
                Close();
        };
        _focusTimer.Start();
    }

    private void StopFocusTimer()
    {
        if (_focusTimer == null) return;
        _focusTimer.Stop();
        _focusTimer = null;
    }

    private void RefreshScripts()
    {
        _scripts = ScriptDiscovery.Discover(_scriptsDir, _registry);
        RenderCards(_scripts);
        StatusLabel.Content =
            $"{_scripts.Count} scripts in {_scriptsDir}";
    }

    private void RenderCards(IEnumerable<ScriptMeta> scripts)
    {
        CardsPanel.Children.Clear();
        foreach (var script in scripts)
        {
            var card = BuildCard(script);
            CardsPanel.Children.Add(card);
        }
    }

    private Border BuildCard(ScriptMeta script)
    {
        var iconText = new TextBlock
        {
            Text = script.Icon,
            FontSize = 28,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var nameText = new TextBlock
        {
            Text = script.Name,
            FontSize = 11,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(4, 4, 4, 0)
        };
        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        stack.Children.Add(iconText);
        stack.Children.Add(nameText);


        var border = new Border
        {
            Width = 140, Height = 100,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(
                Color.FromRgb(0x2A, 0x2A, 0x2A)),
            Cursor = script.HasRun
                ? Cursors.Hand : Cursors.No,
            Tag = script,
            Child = stack
        };

        if (!script.HasRun)
        {
            border.Opacity = 0.4;
            border.ToolTip = !string.IsNullOrWhiteSpace(script.DisabledReason)
                ? script.DisabledReason
                : script.Description;
        }
        else
        {
            border.MouseLeftButtonUp += OnCardClick;
            border.MouseEnter += (_, _) =>
                border.Background = new SolidColorBrush(
                    Color.FromRgb(0x3A, 0x3A, 0x3A));
            border.MouseLeave += (_, _) =>
                border.Background = new SolidColorBrush(
                    Color.FromRgb(0x2A, 0x2A, 0x2A));

            if (!string.IsNullOrWhiteSpace(script.Description))
                border.ToolTip = script.Description;

            border.ContextMenu = BuildCardContextMenu(script);
        }

        return border;
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border) return;
        if (border.Tag is not ScriptMeta script) return;

        Close(); // Close UI immediately.
        App.RunScriptByPath(script.Path);
    }

    private static void ShowNotification(string message)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show(message, "Simpler",
                MessageBoxButton.OK, MessageBoxImage.Information));
    }

    private void OpenScriptsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_scriptsDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = _scriptsDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowNotification("Open scripts failed: " + ex.Message);
        }
    }
    private void SearchBox_TextChanged(object sender,
        TextChangedEventArgs e)
    {
        string q = SearchBox.Text.ToLower();
        var filtered = string.IsNullOrWhiteSpace(q)
            ? _scripts
            : _scripts.Where(s =>
                s.Name.ToLower().Contains(q) ||
                s.Description.ToLower().Contains(q));
        RenderCards(filtered);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private ContextMenu BuildCardContextMenu(ScriptMeta script)
    {
        var menu = new ContextMenu
        {
            StaysOpen = false
        };

        var hotkeyItem = new MenuItem
        {
            Header = "Set Hotkey",
            IsCheckable = true,
            IsEnabled = script.HasRun
        };

        menu.Items.Add(hotkeyItem);

        menu.Opened += (_, _) =>
        {
            hotkeyItem.IsChecked = _hotkeyService.EnsureHotkeyActive(script);
        };

        hotkeyItem.Click += (_, _) =>
        {
            if (!hotkeyItem.IsChecked)
            {
                _hotkeyService.UnregisterScriptHotkey(script);
                _hotkeyService.DeleteHotkeyFile(script);
                return;
            }

            if (!TryCaptureHotkey(out var mods, out var key))
            {
                hotkeyItem.IsChecked = false;
                return;
            }

            if (!_hotkeyService.TryRegisterScriptHotkey(script, mods, key, out var error))
            {
                hotkeyItem.IsChecked = false;
                if (!string.IsNullOrWhiteSpace(error))
                    ShowNotification(error);
                return;
            }

            _hotkeyService.SaveHotkeyFile(script, mods, key);
            var displayName = ScriptHotkeyService.GetScriptDisplayName(script);
            App.ShowToast($"Set hotkey for {displayName}: {ScriptHotkeyService.FormatHotkey(mods, key)}", 3);
        };

        return menu;
    }
    private bool TryCaptureHotkey(out ModifierKeys mods, out Key key)
    {
        mods = ModifierKeys.None;
        key = Key.None;

        var dialog = new Window
        {
            Title = "Set Hotkey",
            Width = 280,
            Height = 120,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Topmost = true
        };

        var text = new TextBlock
        {
            Text = "Press a hotkey (Esc to cancel)",
            Foreground = Brushes.White,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        dialog.Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = text
        };

        bool captured = false;

        ModifierKeys? capturedMods = null;
        Key? capturedKey = null;

        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                dialog.DialogResult = false;
                dialog.Close();
                return;
            }

            var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
            if (IsModifierKey(actualKey))
                return;

            var currentMods = Keyboard.Modifiers;
            if (currentMods == ModifierKeys.None)
            {
                ShowNotification("Hotkey must include Ctrl/Alt/Shift/Win");
                return;
            }

            capturedMods = currentMods;
            capturedKey = actualKey;
            captured = true;
            dialog.DialogResult = true;
            dialog.Close();
        };

        StopFocusTimer();
        try
        {
            dialog.ShowDialog();
        }
        finally
        {
            _openedAt = DateTime.Now;
            StartFocusTimer();
        }

        if (captured && capturedMods.HasValue && capturedKey.HasValue)
        {
            mods = capturedMods.Value;
            key = capturedKey.Value;
        }

        return captured;
    }

    private static string GetScriptDisplayName(ScriptMeta script)
    {
        if (!string.IsNullOrWhiteSpace(script.Name))
            return script.Name;
        if (!string.IsNullOrWhiteSpace(script.FileName))
            return script.FileName;
        return Path.GetFileName(script.Path);
    }

    private static string FormatHotkey(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    public static void CleanupHotkeysOnExit()
    {
        if (_sharedHotkeyService == null)
        {
            _sharedHotkeyService = new ScriptHotkeyService(ResolveScriptsDir(), _ => { });
        }

        _sharedHotkeyService.CleanupHotkeysOnExit();
    }

    private static bool IsModifierKey(Key key)
    {
        return key == Key.LeftShift || key == Key.RightShift ||
               key == Key.LeftCtrl || key == Key.RightCtrl ||
               key == Key.LeftAlt || key == Key.RightAlt ||
               key == Key.LWin || key == Key.RWin;
    }
}






