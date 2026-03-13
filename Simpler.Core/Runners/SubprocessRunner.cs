using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Simpler.Core;
using Simpler.Core.Models;

namespace Simpler.Core.Runners;

public abstract class SubprocessRunner : IScriptRunner
{
    public abstract IReadOnlyList<string> SupportedExtensions { get; }
    public abstract bool HasEntryPoint(string scriptPath);
    protected abstract string ExecutableName { get; }

    // Returns the argument list (not a single string).
    // Each element is a separate argument.
    // Use ProcessStartInfo.ArgumentList, NOT .Arguments.
    protected abstract IEnumerable<string> BuildArgList(string scriptPath);

    public async Task RunAsync(string scriptPath, ScriptContext context)
    {
        try
        {
            // ── 1. Serialize context to JSON ──────────────
            string contextJson = JsonSerializer.Serialize(context);

            // ── 2. Build ProcessStartInfo ─────────────────
            var psi = new ProcessStartInfo
            {
                FileName = ExecutableName,
                WorkingDirectory = Path.GetDirectoryName(scriptPath) 
                                   ?? AppDomain.CurrentDomain.BaseDirectory,
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                StandardInputEncoding  = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8,
            };

            // Use ArgumentList — each element is escaped automatically.
            foreach (var arg in BuildArgList(scriptPath))
                psi.ArgumentList.Add(arg);

            // ── 3. Start process ──────────────────────────
            var process = new Process { StartInfo = psi };
            process.Start();

            // ── 4. Write context JSON to stdin ────────────
            await process.StandardInput.WriteAsync(contextJson);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();

            // ── 5. Read stdout with 30s timeout ──────────
            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(30));
            string resultJson = "";
            string stderr = "";
            try
            {
                var stdoutTask = process.StandardOutput
                    .ReadToEndAsync(cts.Token);
                var stderrTask = process.StandardError
                    .ReadToEndAsync(cts.Token);
                await Task.WhenAll(stdoutTask, stderrTask);
                resultJson = stdoutTask.Result;
                stderr     = stderrTask.Result;
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch { }
                context.NotifyMessage = "Script timed out after 30 seconds";
                Logging.Write($"Timeout: {scriptPath}");
                return;
            }

            await process.WaitForExitAsync();

            // ── 6. Check exit code ────────────────────────
            if (process.ExitCode != 0)
            {
                string detail = stderr.Length > 300
                    ? stderr[..300] : stderr;
                context.NotifyMessage =
                    $"Script error (exit {process.ExitCode}): {detail}";
                Logging.Write($"Exit {process.ExitCode}: {scriptPath}\nSTDERR:\n{stderr}\nSTDOUT:\n{resultJson}");
                return;
            }

            // ── 7. Merge result back into context ─────────
            if (!string.IsNullOrWhiteSpace(resultJson))
            {
                var result = JsonSerializer.Deserialize<ScriptContext>(
                    resultJson);
                if (result != null)
                {
                    context.OutputText     = result.OutputText;
                    context.RenameMap      = result.RenameMap;
                    context.NotifyMessage  = result.NotifyMessage;
                }
            }
        }
        catch (Exception ex)
        {
            context.NotifyMessage = $"Runner error: {ex.Message}";
            Logging.Write($"Runner error: {scriptPath}\n{ex}");
        }
    }
}
