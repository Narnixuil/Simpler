using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jint;
using Simpler.Core;
using Simpler.Core.Models;

namespace Simpler.Core.Runners;

public class JsRunner : IScriptRunner
{
    public IReadOnlyList<string> SupportedExtensions => new[] { ".js" };

    public bool HasEntryPoint(string scriptPath)
    {
        try
        {
            string text = File.ReadAllText(scriptPath);
            return Regex.IsMatch(text,
                @"function\s+run\s*\(|exports\.run\s*=",
                RegexOptions.Multiline);
        }
        catch { return false; }
    }

    public async Task RunAsync(string scriptPath, ScriptContext context)
    {
        // ── Guard: no entry point ─────────────────────────
        if (!HasEntryPoint(scriptPath))
        {
            context.NotifyMessage =
                $"JS error: no run() function found in " +
                $"{Path.GetFileName(scriptPath)}";
            return;
        }

        try
        {
            string scriptText = File.ReadAllText(scriptPath);

            // Jint is synchronous; run on thread pool to avoid 
            // blocking the caller.
            await Task.Run(() =>
            {
                var engine = new Engine(opts =>
                {
                    opts.LimitMemory(50_000_000);            // 50 MB
                    opts.TimeoutInterval(
                        TimeSpan.FromSeconds(30));
                });

                // Expose the ScriptContext .NET object directly.
                // Jint wraps it so JS can call 
                // context.GetSelectedText(), context.SetOutputText(...) etc.
                engine.SetValue("context", context);

                engine.Execute(scriptText + "\nrun(context);");
            });
        }
        catch (Exception ex)
        {
            context.NotifyMessage = $"JS error: {ex.Message}";
            Logging.Write($"JS error: {scriptPath}\n{ex}");
        }
    }
}
