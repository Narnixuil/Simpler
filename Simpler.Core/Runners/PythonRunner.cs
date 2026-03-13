using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Simpler.Core.Models;

namespace Simpler.Core.Runners;

public class PythonRunner : SubprocessRunner
{
    public override IReadOnlyList<string> SupportedExtensions
        => new[] { ".py" };

    protected override string ExecutableName => "python";

    public override bool HasEntryPoint(string scriptPath)
    {
        try
        {
            string text = File.ReadAllText(scriptPath);
            return Regex.IsMatch(text,
                @"^\s*def\s+run\s*\(",
                RegexOptions.Multiline);
        }
        catch { return false; }
    }

    protected override IEnumerable<string> BuildArgList(string scriptPath)
    {
        string wrapperContent = 
"""
import sys, json, importlib.util

ctx_raw = sys.stdin.read()
ctx = json.loads(ctx_raw) if ctx_raw.strip() else {}

spec = importlib.util.spec_from_file_location(
    "user_script", sys.argv[1])
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

mod.run(ctx)
print(json.dumps(ctx))
""";
        string wrapperPath = Path.Combine(
            Path.GetTempPath(), "simpler_py_wrapper.py");
        File.WriteAllText(wrapperPath, wrapperContent, Encoding.UTF8);

        return new[] { wrapperPath, scriptPath };
    }
}
