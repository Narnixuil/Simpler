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
using Microsoft.Win32;
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

    private static readonly Dictionary<string, GlobalHotkey> _scriptHotkeys =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (ModifierKeys Mods, Key Key)> _scriptHotkeyDefs =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _hotkeyOwners =
        new(StringComparer.OrdinalIgnoreCase);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public LauncherWindow()
    {
        InitializeComponent();
        _scriptsDir = ResolveScriptsDir();
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

    private static bool IsScreenshotScript(ScriptMeta script)
    {
        if (!string.IsNullOrWhiteSpace(script.Name) &&
            script.Name.StartsWith("Screenshot", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(script.FileName) &&
            script.FileName.StartsWith("Screenshot", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
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
            hotkeyItem.IsChecked = EnsureHotkeyActive(script);
        };

        hotkeyItem.Click += (_, _) =>
        {
            if (!hotkeyItem.IsChecked)
            {
                UnregisterScriptHotkey(script);
                DeleteHotkeyFile(script);
                return;
            }

            if (!TryCaptureHotkey(out var mods, out var key))
            {
                hotkeyItem.IsChecked = false;
                return;
            }

            if (!TryRegisterScriptHotkey(script, mods, key, out var error))
            {
                hotkeyItem.IsChecked = false;
                if (!string.IsNullOrWhiteSpace(error))
                    ShowNotification(error);
                return;
            }

            SaveHotkeyFile(script, mods, key);
            var displayName = GetScriptDisplayName(script);
            App.ShowToast($"Set hotkey for {displayName}: {FormatHotkey(mods, key)}", 3);
        };

        if (IsScreenshotScript(script))
        {
            menu.Items.Add(new Separator());
            AppendScreenshotContextMenu(menu, script);
        }

        return menu;
    }

    private bool EnsureHotkeyActive(ScriptMeta script)
    {
        if (_scriptHotkeyDefs.ContainsKey(script.Path))
            return true;

        if (!TryLoadHotkeyFile(script, out var mods, out var key))
            return false;

        if (TryRegisterScriptHotkey(script, mods, key, out _))
            return true;

        DeleteHotkeyFile(script);
        return false;
    }

    private bool TryRegisterScriptHotkey(ScriptMeta script, ModifierKeys mods, Key key, out string? error)
    {
        error = null;
        string signature = BuildHotkeySignature(mods, key);

        if (_hotkeyOwners.TryGetValue(signature, out var owner) &&
            !owner.Equals(script.Path, StringComparison.OrdinalIgnoreCase))
        {
            error = "Hotkey is already in use";
            return false;
        }

        UnregisterScriptHotkey(script);

        var hotkey = new GlobalHotkey(mods, key,
            () => App.RunScriptByPath(script.Path));

        if (!hotkey.Register())
        {
            error = "Failed to register hotkey (possibly in use)";
            return false;
        }

        _scriptHotkeys[script.Path] = hotkey;
        _scriptHotkeyDefs[script.Path] = (mods, key);
        _hotkeyOwners[signature] = script.Path;
        return true;
    }

    private void UnregisterScriptHotkey(ScriptMeta script)
    {
        if (_scriptHotkeyDefs.TryGetValue(script.Path, out var def))
        {
            var signature = BuildHotkeySignature(def.Mods, def.Key);
            _hotkeyOwners.Remove(signature);
            _scriptHotkeyDefs.Remove(script.Path);
        }

        if (_scriptHotkeys.TryGetValue(script.Path, out var hotkey))
        {
            hotkey.Unregister();
            _scriptHotkeys.Remove(script.Path);
        }
    }

    private static string BuildHotkeySignature(ModifierKeys mods, Key key)
        => $"{(int)mods}:{key}";

    private bool TryLoadHotkeyFile(ScriptMeta script, out ModifierKeys mods, out Key key)
    {
        mods = ModifierKeys.None;
        key = Key.None;

        try
        {
            string path = GetHotkeySettingsPath(script);
            if (!File.Exists(path)) return false;

            string text = (File.ReadAllText(path) ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split('|');
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], out var modsValue)) return false;
            if (!Enum.TryParse(parts[1], out Key parsedKey)) return false;

            mods = (ModifierKeys)modsValue;
            key = parsedKey;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SaveHotkeyFile(ScriptMeta script, ModifierKeys mods, Key key)
    {
        try
        {
            string path = GetHotkeySettingsPath(script);
            File.WriteAllText(path, $"{(int)mods}|{key}");
        }
        catch (Exception ex)
        {
            ShowNotification("Failed to save hotkey: " + ex.Message);
        }
    }

    private void DeleteHotkeyFile(ScriptMeta script)
    {
        try
        {
            string path = GetHotkeySettingsPath(script);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private string GetHotkeySettingsPath(ScriptMeta script)
    {
        string dir = Path.GetDirectoryName(script.Path) ?? _scriptsDir;
        string name = Path.GetFileNameWithoutExtension(script.Path);
        return Path.Combine(dir, $"_{name}.hotkey.txt");
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
        foreach (var hotkey in _scriptHotkeys.Values)
            hotkey.Unregister();

        _scriptHotkeys.Clear();
        _scriptHotkeyDefs.Clear();
        _hotkeyOwners.Clear();

        try
        {
            string scriptsDir = ResolveScriptsDir();
            if (!Directory.Exists(scriptsDir)) return;

            foreach (var file in Directory.EnumerateFiles(
                         scriptsDir, "_*.hotkey.txt", SearchOption.AllDirectories))
            {
                try { File.Delete(file); }
                catch { }
            }
        }
        catch { }
    }

    private static bool IsModifierKey(Key key)
    {
        return key == Key.LeftShift || key == Key.RightShift ||
               key == Key.LeftCtrl || key == Key.RightCtrl ||
               key == Key.LeftAlt || key == Key.RightAlt ||
               key == Key.LWin || key == Key.RWin;
    }

    private void AppendScreenshotContextMenu(ContextMenu menu, ScriptMeta script)
    {
        var systemItem = new MenuItem
        {
            Header = "Screenshot Tool: System (Default)",
            IsCheckable = true
        };
        var customItem = new MenuItem
        {
            Header = "Screenshot Tool: Custom...",
            IsCheckable = true
        };

        menu.Opened += (_, _) =>
        {
            var cfg = ReadScreenshotToolConfig(script);
            systemItem.IsChecked = cfg.Tool == "system";
            customItem.IsChecked = cfg.Tool == "custom";
        };

        systemItem.Click += (_, _) =>
        {
            WriteScreenshotToolConfig(script, "system", "");
        };

        customItem.Click += (_, _) =>
        {
            string path = PickScreenshotToolPath();
            if (!string.IsNullOrWhiteSpace(path))
                WriteScreenshotToolConfig(script, "custom", path);
        };

        menu.Items.Add(systemItem);
        menu.Items.Add(customItem);
    }

    private string PickScreenshotToolPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Screenshot Tool",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true
        };

        StopFocusTimer();
        try
        {
            bool? result = dialog.ShowDialog(this);
            return result == true ? dialog.FileName : "";
        }
        finally
        {
            _openedAt = DateTime.Now;
            StartFocusTimer();
        }
    }

    private ScreenshotToolConfig ReadScreenshotToolConfig(ScriptMeta script)
    {
        var cfg = new ScreenshotToolConfig();
        try
        {
            string settingsPath = GetScreenshotSettingsPath(script);
            if (!File.Exists(settingsPath)) return cfg;

            string text = (File.ReadAllText(settingsPath) ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text)) return cfg;

            if (text.StartsWith("custom|", StringComparison.OrdinalIgnoreCase))
            {
                cfg.Tool = "custom";
                cfg.Path = text.Substring("custom|".Length).Trim();
                return cfg;
            }

            if (text.Equals("custom", StringComparison.OrdinalIgnoreCase))
            {
                cfg.Tool = "custom";
                return cfg;
            }

            if (text.Equals("system", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                cfg.Tool = "system";
                return cfg;
            }
        }
        catch { }

        return cfg;
    }

    private void WriteScreenshotToolConfig(ScriptMeta script, string tool, string path)
    {
        try
        {
            string settingsPath = GetScreenshotSettingsPath(script);
            string content = tool == "custom" && !string.IsNullOrWhiteSpace(path)
                ? "custom|" + path
                : "system";
            File.WriteAllText(settingsPath, content);
        }
        catch (Exception ex)
        {
            ShowNotification("Failed to save screenshot tool: " + ex.Message);
        }
    }

    private string GetScreenshotSettingsPath(ScriptMeta script)
    {
        string dir = Path.GetDirectoryName(script.Path) ?? _scriptsDir;
        return Path.Combine(dir, "_screenshot_tool.txt");
    }

    private sealed class ScreenshotToolConfig
    {
        public string Tool { get; set; } = "system";
        public string Path { get; set; } = "";
    }
}


