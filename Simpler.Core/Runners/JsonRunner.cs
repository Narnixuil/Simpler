using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
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
}
