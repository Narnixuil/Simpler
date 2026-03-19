using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Simpler.Core.Models;

namespace Simpler.Core.Context;

public static class ContextCapture
{
    public enum CaptureMode
    {
        ActiveCopy,
        Passive
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    public static Task<ScriptContext> GrabAsync(CaptureMode mode = CaptureMode.ActiveCopy)
    {
        return Task.Run(() => StaRunner.Run(() => GrabImpl(mode)));
    }

    // Called on STA thread by StaRunner.
    private static ScriptContext GrabImpl(CaptureMode mode)
    {
        string selectedText = mode == CaptureMode.ActiveCopy
            ? CaptureSelectedTextByClipboardRoundtrip()
            : "";

        var selectedFiles = CaptureSelectedFiles();
        string activeTitle = CaptureActiveWindowTitle();

        return new ScriptContext
        {
            SelectedText = selectedText,
            SelectedFiles = selectedFiles,
            ActiveWindowTitle = activeTitle
        };
    }

    private static string CaptureSelectedTextByClipboardRoundtrip()
    {
        // 1) Save old clipboard.
        IDataObject? oldClip = null;
        string oldText = "";
        bool oldHasText = false;
        try
        {
            oldClip = Clipboard.GetDataObject();
            if (Clipboard.ContainsText())
            {
                oldText = Clipboard.GetText();
                oldHasText = !string.IsNullOrEmpty(oldText);
            }
        }
        catch { }

        // 2) Clear clipboard.
        try { Clipboard.Clear(); } catch { }

        // 3) Simulate Ctrl+C.
        try { SendKeys.SendWait("^c"); } catch { }

        // 4) Wait for clipboard to populate.
        string selectedText = "";
        for (int i = 0; i < 10; i++)
        {
            System.Threading.Thread.Sleep(30);
            try { selectedText = Clipboard.GetText(); } catch { selectedText = ""; }
            if (!string.IsNullOrEmpty(selectedText)) break;
        }

        // 5) Restore old clipboard.
        try
        {
            // Prefer explicit text restore to avoid delayed-data clipboard glitches.
            if (oldHasText)
                Clipboard.SetText(oldText);
            else if (oldClip != null)
                Clipboard.SetDataObject(oldClip, true);
            else
                Clipboard.Clear();
        }
        catch { }

        return selectedText;
    }

    private static List<string> CaptureSelectedFiles()
    {
        var selectedFiles = new List<string>();
        try
        {
            IntPtr fgHwnd = GetForegroundWindow();
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return selectedFiles;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic windows = shell.Windows();
            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic win = windows.Item(i);
                    if (win == null) continue;
                    string loc = win.LocationURL ?? "";
                    if (string.IsNullOrEmpty(loc)) continue;

                    IntPtr winHwnd = (IntPtr)(long)win.HWND;
                    if (winHwnd != fgHwnd) continue;

                    dynamic items = win.Document.SelectedItems();
                    int itemCount = items.Count;
                    for (int j = 0; j < itemCount; j++)
                    {
                        try
                        {
                            string path = items.Item(j).Path;
                            if (!string.IsNullOrEmpty(path))
                                selectedFiles.Add(path);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        catch { }

        return selectedFiles;
    }

    private static string CaptureActiveWindowTitle()
    {
        try
        {
            var sb = new StringBuilder(512);
            GetWindowText(GetForegroundWindow(), sb, sb.Capacity);
            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }
}
