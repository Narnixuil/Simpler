using System;
using System.Threading.Tasks;
using System.Windows;
using Simpler.Core;

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

        // Register Ctrl+` as global hotkey.
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

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Unregister();
        _tray?.Dispose();
        base.OnExit(e);
    }
}

