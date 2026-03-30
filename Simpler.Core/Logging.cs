using System;
using System.IO;
using System.Text;

namespace Simpler.Core;

public static class Logging
{
    private static readonly object _lock = new();

    public static void Write(string message)
    {
#if !DEBUG
        return;
#else
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string logDir = Path.Combine(baseDir, "logs");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, "simpler.log");

            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            lock (_lock)
            {
                File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Swallow logging errors to avoid breaking runtime flow.
        }
#endif
    }
}
