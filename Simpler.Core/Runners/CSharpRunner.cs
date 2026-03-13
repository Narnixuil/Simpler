using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Simpler.Core.Context;
using Simpler.Core.Models;

namespace Simpler.Core.Runners;

public class CSharpRunner : IScriptRunner
{
    public IReadOnlyList<string> SupportedExtensions => new[] { ".csx" };

    private static IReadOnlyList<MetadataReference>? _cachedRefs;
    private static readonly object _refLock = new();

    public bool HasEntryPoint(string scriptPath)
    {
        try
        {
            string text = File.ReadAllText(scriptPath);
            return Regex.IsMatch(text,
                @"^\s*(?:public\s+)?(?:static\s+)?(?:async\s+)?" +
                @"(?:System\.Threading\.Tasks\.)?Task\s+Run\s*\(" +
                @"|^\s*void\s+Run\s*\(",
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
                .WithReferences(GetReferences())
                .WithImports(
                    "System",
                    "System.IO",
                    "System.Linq",
                    "System.Collections.Generic",
                    "System.Threading",
                    "System.Threading.Tasks",
                    "System.Text",
                    "System.Text.RegularExpressions",
                    "System.Net.Http",
                    "System.Windows.Forms",
                    "System.Drawing",
                    "Simpler.Core",
                    "Simpler.Core.Models",
                    "Simpler.Core.Context");

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

    private static IReadOnlyList<MetadataReference> GetReferences()
    {
        if (_cachedRefs != null) return _cachedRefs;
        lock (_refLock)
        {
            if (_cachedRefs != null) return _cachedRefs;
            _cachedRefs = BuildReferences();
            return _cachedRefs;
        }
    }

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var refs = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(tpa))
        {
            foreach (var path in tpa.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;

                if (seen.Add(path))
                    refs.Add(MetadataReference.CreateFromFile(path));
            }
        }

        // Ensure Simpler.Core is available when published as single-file.
        TryAddFile(refs, seen, Path.Combine(AppContext.BaseDirectory, "Simpler.Core.dll"));

        // Fallback for dev environments where TPA may be incomplete.
#pragma warning disable IL3000
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            if (string.IsNullOrWhiteSpace(asm.Location)) continue;
            if (!File.Exists(asm.Location)) continue;

            if (seen.Add(asm.Location))
                refs.Add(MetadataReference.CreateFromFile(asm.Location));
        }
#pragma warning restore IL3000

        AddAssemblyLocation(refs, seen, typeof(ScriptContext).Assembly);
        AddAssemblyLocation(refs, seen, typeof(StaRunner).Assembly);

        return refs;
    }

    private static void TryAddFile(
        List<MetadataReference> refs,
        HashSet<string> seen,
        string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        if (seen.Add(path))
            refs.Add(MetadataReference.CreateFromFile(path));
    }

    private static void AddAssemblyLocation(
        List<MetadataReference> refs,
        HashSet<string> seen,
        Assembly assembly)
    {
        if (assembly.IsDynamic) return;
#pragma warning disable IL3000
        if (string.IsNullOrWhiteSpace(assembly.Location)) return;
        if (!File.Exists(assembly.Location)) return;

        if (seen.Add(assembly.Location))
            refs.Add(MetadataReference.CreateFromFile(assembly.Location));
#pragma warning restore IL3000
    }
}

public class CSharpScriptGlobals
{
    public ScriptContext context { get; set; } = new();
}
