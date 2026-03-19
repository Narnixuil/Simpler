using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Simpler.Core;
using Simpler.Core.Context;
using Simpler.Core.Models;

namespace Simpler.Host;

public partial class App : Application
{
    public static RunnerRegistry Registry { get; private set; } = new();
    private static LauncherWindow? _launcher;
    private TrayManager? _tray;
    private GlobalHotkey? _hotkey;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception logging to prevent silent exits.
        DispatcherUnhandledException += (_, args) =>
        {
            Logging.Write($"Dispatcher exception: {args.Exception}");
            MessageBox.Show(args.Exception.Message, "Simpler", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Logging.Write($"Unhandled exception: {args.ExceptionObject}");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logging.Write($"Unobserved task exception: {args.Exception}");
            args.SetObserved();
        };

        // Ensure a WPF window has been created so Dispatcher works.
        var dummy = new Window
        {
            Width = 0, Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Visibility = Visibility.Hidden
        };
        dummy.Show();
        dummy.Hide();

        Registry = new RunnerRegistry();

        _tray = new TrayManager();
        _tray.Initialize();

        ShowStartupToast();

        // Register Ctrl+ as global hotkey.
        // Use GlobalHotkey helper (defined in TrayManager.cs below).
        _hotkey = new GlobalHotkey(
            System.Windows.Input.ModifierKeys.Control,
            System.Windows.Input.Key.OemTilde,
            ShowLauncher);
        _hotkey.Register();
    }

    public static void ShowLauncher()
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_launcher != null &&
                _launcher.IsVisible)
            {
                _launcher.Close();
                _launcher = null;
                return;
            }
            _launcher = new LauncherWindow();
            _launcher.Closed += (_, _) => _launcher = null;
            _launcher.Show();
            _launcher.Activate();
        });
    }

    public static void RunScriptByPath(string scriptPath)
    {
        _ = Task.Run(async () =>
        {
            ScriptContext context;
            try
            {
                var captureMode = ResolveCaptureMode(scriptPath);
                context = await ContextCapture.GrabAsync(captureMode);
            }
            catch (Exception ex)
            {
                ShowNotification($"Capture error: {ex.Message}");
                return;
            }

            var runner = Registry.GetRunner(Path.GetExtension(scriptPath));
            if (runner == null)
            {
                ShowNotification("No runner for this file type");
                return;
            }

            await runner.RunAsync(scriptPath, context);

            try
            {
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


    private static ContextCapture.CaptureMode ResolveCaptureMode(string scriptPath)
    {
        try
        {
            string global = Environment.GetEnvironmentVariable("SIMPLER_CAPTURE_MODE") ?? "";
            if (global.Equals("passive", StringComparison.OrdinalIgnoreCase))
                return ContextCapture.CaptureMode.Passive;

            if (Path.GetExtension(scriptPath).Equals(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(scriptPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(scriptPath));
                if (doc.RootElement.TryGetProperty("captureMode", out var cm) &&
                    cm.ValueKind == JsonValueKind.String)
                {
                    string value = (cm.GetString() ?? "").Trim();
                    if (value.Equals("passive", StringComparison.OrdinalIgnoreCase))
                        return ContextCapture.CaptureMode.Passive;
                }
            }
        }
        catch { }

        return ContextCapture.CaptureMode.ActiveCopy;
    }
    public static void ShowNotification(string message)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
            MessageBox.Show(message, "Simpler",
                MessageBoxButton.OK, MessageBoxImage.Information));
    }

    private static void ShowStartupToast()
    {
        ShowToast("Simpler is ready", 3);
    }

    public static void ShowToast(string message, int seconds = 3)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var toast = new Window
            {
                Width = 320,
                Height = 44,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize
            };

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 30, 30, 30)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8)
            };

            var text = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };

            border.Child = text;
            toast.Content = border;

            var work = SystemParameters.WorkArea;
            toast.Left = work.Right - toast.Width - 16;
            toast.Top = work.Bottom - toast.Height - 16;

            toast.Show();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                toast.Close();
            };
            timer.Start();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Unregister();
        _tray?.Dispose();
        LauncherWindow.CleanupHotkeysOnExit();
        base.OnExit(e);
    }
}

