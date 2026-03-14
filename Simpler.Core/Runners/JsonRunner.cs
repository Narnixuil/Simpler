using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Simpler.Core.Context;
using Simpler.Core.Models;

namespace Simpler.Core.Runners;

public class JsonRunner : IScriptRunner
{
    public IReadOnlyList<string> SupportedExtensions => new[] { ".json" };

    private class ExecState
    {
        public string Text { get; set; } = "";
        public List<string> Files { get; set; } = new();
    }

    public bool HasEntryPoint(string scriptPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(scriptPath));
            return doc.RootElement.TryGetProperty("steps", out var steps) &&
                   steps.ValueKind == JsonValueKind.Array;
        }
        catch
        {
            return false;
        }
    }

    public async Task RunAsync(string scriptPath, ScriptContext context)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(scriptPath));
            var root = doc.RootElement;
            if (!root.TryGetProperty("steps", out var steps) ||
                steps.ValueKind != JsonValueKind.Array)
            {
                context.NotifyMessage = "Invalid JSON script (missing steps).";
                return;
            }

            var state = new ExecState
            {
                Text = context.SelectedText ?? string.Empty,
                Files = new List<string>(context.SelectedFiles)
            };

            foreach (var step in steps.EnumerateArray())
            {
                if (!step.TryGetProperty("op", out var opEl)) continue;
                string op = (opEl.GetString() ?? "").Trim().ToLowerInvariant();

                switch (op)
                {
                    case "getselectedtext":
                        state.Text = context.SelectedText ?? "";
                        break;
                    case "getselectedfiles":
                        state.Files = new List<string>(context.SelectedFiles);
                        break;
                    case "setoutputtext":
                        context.OutputText = GetString(step, "value", state.Text);
                        break;
                    case "setclipboard":
                        SetClipboard(GetString(step, "value", state.Text));
                        break;
                    case "paste":
                        Paste();
                        break;
                    case "simulatekey":
                    case "sendkeys":
                        SimulateKeys(step);
                        break;
                    case "simulateinput":
                    case "typeinput":
                        SimulateInput(step);
                        break;
                    case "exec":
                        ExecExternal(step, context, state);
                        break;
                    case "js":
                    case "python":
                    case "csharp":
                        await RunInlineCodeAsync(op, step, context);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            context.NotifyMessage = $"JSON error: {ex.Message}";
            Logging.Write($"JSON error: {scriptPath}\n{ex}");
        }
    }

    private static string GetString(JsonElement step, string name, string? fallback = "")
    {
        return step.TryGetProperty(name, out var v) ? v.GetString() ?? fallback ?? "" : fallback ?? "";
    }

    private static string GetCode(JsonElement step)
    {
        if (step.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String)
            return c.GetString() ?? "";

        if (step.TryGetProperty("codeLines", out var lines) && lines.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var el in lines.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                    list.Add(el.GetString() ?? "");
            }
            return string.Join("\n", list);
        }

        return "";
    }

    private static bool GetBool(JsonElement step, string name, bool fallback = false)
    {
        return step.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True
            ? true
            : step.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.False
                ? false
                : fallback;
    }

    private static int GetInt(JsonElement step, string name, int fallback = 0)
    {
        if (!step.TryGetProperty(name, out var v))
            return fallback;

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i))
            return i;

        if (v.ValueKind == JsonValueKind.String &&
            int.TryParse(v.GetString(), out i))
            return i;

        return fallback;
    }

    private static IEnumerable<string> GetStringArray(JsonElement step, string name)
    {
        if (!step.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
                list.Add(el.GetString() ?? "");
        }
        return list;
    }

    private static void ExecExternal(JsonElement step, ScriptContext context, ExecState state)
    {
        string file = GetString(step, "file");
        if (string.IsNullOrWhiteSpace(file))
        {
            context.NotifyMessage = "exec: missing file.";
            return;
        }

        var args = GetStringArray(step, "args").ToList();
        bool wait = GetBool(step, "wait", true);
        bool shell = GetBool(step, "shell", true);

        var psi = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = shell
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        var p = Process.Start(psi);
        if (wait && p != null)
            p.WaitForExit();
    }

    private static async Task RunInlineCodeAsync(string op, JsonElement step, ScriptContext context)
    {
        string code = GetCode(step);
        if (string.IsNullOrWhiteSpace(code)) return;

        string tempDir = Path.Combine(Path.GetTempPath(), "simpler_inline");
        Directory.CreateDirectory(tempDir);

        if (op == "js")
        {
            if (!Regex.IsMatch(code, @"function\s+run\s*\(|exports\.run\s*="))
                code = "function run(context){\n" + code + "\n}";
            string path = Path.Combine(tempDir, "inline.js");
            File.WriteAllText(path, code);
            var runner = new JsRunner();
            await runner.RunAsync(path, context);
        }
        else if (op == "python")
        {
            if (!Regex.IsMatch(code, @"^\s*def\s+run\s*\(", RegexOptions.Multiline))
                code = "def run(ctx):\n    " + code.Replace("\n", "\n    ");
            string path = Path.Combine(tempDir, "inline.py");
            File.WriteAllText(path, code);
            var runner = new PythonRunner();
            await runner.RunAsync(path, context);
        }
        else if (op == "csharp")
        {
            if (!Regex.IsMatch(code, @"\bRun\s*\(", RegexOptions.Multiline))
            {
                code = "using Simpler.Core.Models;\n" +
                       "async Task Run(ScriptContext context)\n{\n" +
                       code + "\n}";
            }
            string path = Path.Combine(tempDir, "inline.csx");
            File.WriteAllText(path, code);
            var runner = new CSharpRunner();
            await runner.RunAsync(path, context);
        }
    }

    private static void SetClipboard(string text)
    {
        try
        {
            StaRunner.Run(() => Clipboard.SetText(text ?? ""));
        }
        catch { }
    }

    private static void Paste()
    {
        try
        {
            StaRunner.Run(() => SendKeys.SendWait("^v"));
        }
        catch { }
    }

    private static void SimulateKeys(JsonElement step)
    {
        string hotkey = GetString(step, "keys",
            GetString(step, "key", ""));
        if (string.IsNullOrWhiteSpace(hotkey)) return;

        bool normalize = GetBool(step, "normalize", true);
        int preDelay = GetInt(step, "preDelayMs", 0);
        int postDelay = GetInt(step, "postDelayMs", 0);

        string keys = normalize ? ToSendKeys(hotkey) : hotkey;
        if (string.IsNullOrWhiteSpace(keys)) return;

        try
        {
            StaRunner.Run(() =>
            {
                if (preDelay > 0) Thread.Sleep(preDelay);
                SendKeys.SendWait(keys);
                if (postDelay > 0) Thread.Sleep(postDelay);
            });
        }
        catch { }
    }

    private static void SimulateInput(JsonElement step)
    {
        string text = GetString(step, "text",
            GetString(step, "value", ""));

        int perCharDelay = GetInt(step, "perCharDelayMs", 0);
        int preDelay = GetInt(step, "preDelayMs", 0);
        int postDelay = GetInt(step, "postDelayMs", 0);
        bool ensureEnglish = GetBool(step, "ensureEnglish", false);


        try
        {
            StaRunner.Run(() =>
            {
                if (ensureEnglish)
                    EnsureEnglishInput();

                if (preDelay > 0) Thread.Sleep(preDelay);

                if (perCharDelay > 0)
                    TypeUnicodeSlow(text ?? "", perCharDelay);
                else
                    TypeUnicode(text ?? "");

                if (postDelay > 0) Thread.Sleep(postDelay);
            });
        }
        catch { }
    }
    private static string ToSendKeys(string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey)) return "";
        return hotkey
            .Replace(" ", "")
            .Replace("Ctrl+", "^")
            .Replace("Alt+", "%")
            .Replace("Shift+", "+")
            .Replace("Win+", "#")
            .ToLowerInvariant();
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private const uint KLF_ACTIVATE = 0x00000001;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

    [DllImport("user32.dll")] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("imm32.dll")] private static extern IntPtr ImmGetContext(IntPtr hWnd);
    [DllImport("imm32.dll")] private static extern bool ImmSetOpenStatus(IntPtr hIMC, bool fOpen);
    [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private static void EnsureEnglishInput()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        var hIMC = ImmGetContext(hwnd);
        if (hIMC != IntPtr.Zero)
        {
            ImmSetOpenStatus(hIMC, false);
            ImmReleaseContext(hwnd, hIMC);
        }

        var hkl = LoadKeyboardLayout("00000409", KLF_ACTIVATE);
        SendMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
    }



    private static void TypeUnicode(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (var ch in text)
        {
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = KEYEVENTF_UNICODE }
                }
            });
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP }
                }
            });
        }

        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        if (sent == 0)
            SendKeys.SendWait(text);
    }

    private static void TypeUnicodeSlow(string text, int perCharDelayMs)
    {
        if (string.IsNullOrEmpty(text)) return;

        foreach (var ch in text)
        {
            var inputs = new INPUT[2];
            inputs[0] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = KEYEVENTF_UNICODE }
                }
            };
            inputs[1] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP }
                }
            };

            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
            Thread.Sleep(perCharDelayMs);
        }
    }
}



