using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        RenderCards(GetFilteredScripts(SearchBox.Text));
        StatusLabel.Content =
            $"{_scripts.Count} scripts in {_scriptsDir}";
    }

    private IEnumerable<ScriptMeta> GetFilteredScripts(string query)
    {
        var q = (query ?? string.Empty).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(q))
            return _scripts;

        return _scripts.Where(s =>
            s.Name.ToLowerInvariant().Contains(q) ||
            s.Description.ToLowerInvariant().Contains(q));
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
        }

        border.ContextMenu = BuildCardContextMenu(script);

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
    private void QuickCreateScriptButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryShowCreateScriptDialog(
                out var scriptName,
                out var icon,
                out var description))
            return;

        try
        {
            Directory.CreateDirectory(_scriptsDir);
            var safeName = MakeSafeFileName(scriptName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                ShowNotification("Script name is invalid.");
                return;
            }

            var targetPath = Path.Combine(_scriptsDir, $"{safeName}.json");
            if (File.Exists(targetPath))
            {
                ShowNotification("A script with this name already exists.");
                return;
            }

            var templatePath = Path.Combine(_scriptsDir, "Template.json");
            var templateText = File.Exists(templatePath)
                ? File.ReadAllText(templatePath)
                : """
                  {
                    "name": "Template",
                    "icon": "icon",
                    "description": "Script description",
                    "steps": [
                      {
                        "op": "csharp",
                        "codeLines": []
                      }
                    ]
                  }
                  """;

            var root = JsonNode.Parse(templateText)?.AsObject();
            if (root is null)
            {
                ShowNotification("Template.json is invalid.");
                return;
            }

            root["name"] = scriptName;
            root["icon"] = icon;
            root["description"] = description;

            var json = root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(targetPath, json + Environment.NewLine, new UTF8Encoding(false));

            Close();
            Process.Start(new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = true
            });
            App.ShowToast($"Created script: {scriptName}", 2);
        }
        catch (Exception ex)
        {
            ShowNotification("Create script failed: " + ex.Message);
        }
    }
    private void SearchBox_TextChanged(object sender,
        TextChangedEventArgs e)
    {
        RenderCards(GetFilteredScripts(SearchBox.Text));
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


        var editItem = new MenuItem
        {
            Header = "Edit"
        };
        editItem.Click += (_, _) => OpenScriptFile(script);
        menu.Items.Add(editItem);

        menu.Items.Add(new Separator());
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

    private void OpenScriptFile(ScriptMeta script)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(script.Path) || !File.Exists(script.Path))
            {
                ShowNotification("Script file not found.");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = script.Path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowNotification("Open script file failed: " + ex.Message);
        }
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
    private bool TryShowCreateScriptDialog(
        out string scriptName,
        out string icon,
        out string description)
    {
        scriptName = string.Empty;
        icon = string.Empty;
        description = string.Empty;

        var selectedName = string.Empty;
        var selectedIcon = string.Empty;
        var selectedDescription = string.Empty;

        var dialog = new Window
        {
            Title = "Quick Create Script",
            Width = 420,
            Height = 240,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Topmost = true,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A))
        };

        var panel = new Grid
        {
            Margin = new Thickness(14)
        };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBrush = Brushes.White;
        var boxBackground = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
        var boxBorder = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

        var nameLabel = new TextBlock
        {
            Text = "Script Name",
            Foreground = labelBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(nameLabel, 0);
        Grid.SetColumn(nameLabel, 0);

        var nameBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(6, 4, 6, 4),
            Background = boxBackground,
            BorderBrush = boxBorder,
            Foreground = Brushes.White
        };
        Grid.SetRow(nameBox, 0);
        Grid.SetColumn(nameBox, 1);

        var iconLabel = new TextBlock
        {
            Text = "Icon",
            Foreground = labelBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(iconLabel, 1);
        Grid.SetColumn(iconLabel, 0);

        var iconBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(6, 4, 6, 4),
            Background = boxBackground,
            BorderBrush = boxBorder,
            Foreground = Brushes.White,
            Text = "icon"
        };
        Grid.SetRow(iconBox, 1);
        Grid.SetColumn(iconBox, 1);

        var descLabel = new TextBlock
        {
            Text = "Description",
            Foreground = labelBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(descLabel, 2);
        Grid.SetColumn(descLabel, 0);

        var descBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(6, 4, 6, 4),
            Background = boxBackground,
            BorderBrush = boxBorder,
            Foreground = Brushes.White
        };
        Grid.SetRow(descBox, 2);
        Grid.SetColumn(descBox, 1);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(buttons, 4);
        Grid.SetColumnSpan(buttons, 2);

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 100,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };
        cancelButton.Click += (_, _) =>
        {
            dialog.DialogResult = false;
            dialog.Close();
        };

        var startButton = new Button
        {
            Content = "Start Writing Script",
            MinWidth = 160,
            Height = 32
        };
        startButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                ShowNotification("Script name is required.");
                nameBox.Focus();
                return;
            }

            selectedName = nameBox.Text.Trim();
            selectedIcon = string.IsNullOrWhiteSpace(iconBox.Text) ? "icon" : iconBox.Text.Trim();
            selectedDescription = string.IsNullOrWhiteSpace(descBox.Text)
                ? "TODO: describe this script"
                : descBox.Text.Trim();
            dialog.DialogResult = true;
            dialog.Close();
        };

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(startButton);

        panel.Children.Add(nameLabel);
        panel.Children.Add(nameBox);
        panel.Children.Add(iconLabel);
        panel.Children.Add(iconBox);
        panel.Children.Add(descLabel);
        panel.Children.Add(descBox);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        StopFocusTimer();
        try
        {
            nameBox.Focus();
            var result = dialog.ShowDialog();
            if (result == true)
            {
                scriptName = selectedName;
                icon = selectedIcon;
                description = selectedDescription;
                return true;
            }

            return false;
        }
        finally
        {
            _openedAt = DateTime.Now;
            StartFocusTimer();
        }
    }

    private static string MakeSafeFileName(string scriptName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(scriptName
            .Trim()
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray());
        return cleaned.Trim();
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

