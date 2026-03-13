using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Simpler.Core.Models;

namespace Simpler.Core.Runners;

public class CSharpRunner : IScriptRunner
{
    public IReadOnlyList<string> SupportedExtensions => new[] { ".csx" };

    public bool HasEntryPoint(string scriptPath)
    {
        try
        {
            string text = File.ReadAllText(scriptPath);
            return Regex.IsMatch(text,
                @"^\s*(?:public\s+)?(?:static\s+)?(?:async\s+)?" +
                @"Task\s+Run\s*\(|^\s*void\s+Run\s*\(",
                RegexOptions.Multiline);
        }
        catch { return false; }
    }

    public async Task RunAsync(string scriptPath, ScriptContext context)
    {
        try
        {
            string scriptText = File.ReadAllText(scriptPath);

            if (HasEntryPoint(scriptPath))
                scriptText += "\nawait Run(context);";

            var options = ScriptOptions.Default
                .WithReferences(
                    typeof(ScriptContext).Assembly,
                    typeof(object).Assembly,
                    Assembly.Load("System.Runtime"),
                    Assembly.Load("System.IO.FileSystem"),
                    Assembly.Load("System.Linq"),
                    Assembly.Load("System.Collections"),
                    Assembly.Load("System.Net.Http"),
                    Assembly.Load("System.Windows.Forms"),
                    Assembly.Load("System.Drawing"))
                .WithImports(
                    "System",
                    "System.IO",
                    "System.Linq",
                    "System.Collections.Generic",
                    "System.Threading.Tasks",
                    "System.Text",
                    "System.Text.RegularExpressions",
                    "System.Net.Http",
                    "System.Windows.Forms",
                    "System.Drawing",
                    "Simpler.Core",
                    "Simpler.Core.Models");

            var globals = new CSharpScriptGlobals { context = context };

            await CSharpScript.RunAsync(
                scriptText, options, globals,
                typeof(CSharpScriptGlobals));
        }
        catch (Exception ex)
        {
            context.NotifyMessage = $"C# error: {ex.Message}";
            Logging.Write($"C# error: {scriptPath}\n{ex}");
        }
    }
}

public class CSharpScriptGlobals
{
    public ScriptContext context { get; set; } = new();
}
