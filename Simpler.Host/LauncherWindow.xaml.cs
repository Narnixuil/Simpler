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
            $"{_scripts.Count} scripts · {_scriptsDir}";
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
        var langText = new TextBlock
        {
            Text = script.Lang.ToUpper(),
            FontSize = 8,
            Foreground = new SolidColorBrush(
                Color.FromRgb(0x66, 0x66, 0x66)),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        stack.Children.Add(iconText);
        stack.Children.Add(nameText);
        stack.Children.Add(langText);

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
            border.ToolTip = script.DisabledReason;
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
        }

        return border;
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border) return;
        if (border.Tag is not ScriptMeta script) return;

        Close(); // Close UI immediately.

        // All heavy work on background thread.
        _ = Task.Run(async () =>
        {
            ScriptContext context;
            try
            {
                // ContextCapture internally uses StaRunner — safe here.
                context = await ContextCapture.GrabAsync();
            }
            catch (Exception ex)
            {
                ShowNotification($"Capture error: {ex.Message}");
                return;
            }

            var runner = _registry.GetRunner(
                Path.GetExtension(script.Path));
            if (runner == null)
            {
                ShowNotification("No runner for this file type");
                return;
            }

            await runner.RunAsync(script.Path, context);

            try
            {
                // ContextApply internally uses StaRunner — safe here.
                await ContextApply.CommitAsync(context);
            }
            catch (Exception ex)
            {
                context.NotifyMessage ??= $"Apply error: {ex.Message}";
            }

            if (!string.IsNullOrEmpty(context.NotifyMessage))
                ShowNotification(context.NotifyMessage);
        });
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
}











