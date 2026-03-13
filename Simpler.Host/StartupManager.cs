using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Simpler.Host;

public static class StartupManager
{
    private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string AppName = "Simpler";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            var value = key?.GetValue(AppName) as string;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var exe = GetExecutablePath();
            if (string.IsNullOrWhiteSpace(exe)) return false;
            return value.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);

            if (enabled)
            {
                var exe = GetExecutablePath();
                if (string.IsNullOrWhiteSpace(exe)) return;
                key?.SetValue(AppName, "\"" + exe + "\"");
            }
            else
            {
                key?.DeleteValue(AppName, false);
            }
        }
        catch
        {
            // Ignore startup registration errors.
        }
    }

    public static void EnsureEnabledIfMissing()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            var value = key?.GetValue(AppName) as string;
            if (!string.IsNullOrWhiteSpace(value)) return;
        }
        catch
        {
            return;
        }

        SetEnabled(true);
    }

    private static string GetExecutablePath()
    {
        try
        {
            return Environment.ProcessPath
                   ?? Process.GetCurrentProcess().MainModule?.FileName
                   ?? "";
        }
        catch
        {
            return "";
        }
    }
}
