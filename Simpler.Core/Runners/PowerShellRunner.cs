using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Simpler.Core.Models;

namespace Simpler.Core.Runners;

public class PowerShellRunner : SubprocessRunner
{
    public override IReadOnlyList<string> SupportedExtensions
        => new[] { ".ps1" };

    protected override string ExecutableName => "powershell";

    public override bool HasEntryPoint(string scriptPath)
    {
        try
        {
            string text = File.ReadAllText(scriptPath);
            return Regex.IsMatch(text,
                @"^\s*function\s+[Rr]un\s*[\(\{]",
                RegexOptions.Multiline);
        }
        catch { return false; }
    }

    protected override IEnumerable<string> BuildArgList(string scriptPath)
    {
        string wrapperContent =
"""
param([string]$ScriptPath)
$ctx = $input | ConvertFrom-Json -AsHashtable
. $ScriptPath
Run $ctx
$ctx | ConvertTo-Json -Depth 10
""";
        string wrapperPath = Path.Combine(
            Path.GetTempPath(), "simpler_ps1_wrapper.ps1");
        File.WriteAllText(wrapperPath, wrapperContent, Encoding.UTF8);

        return new[]
        {
            "-ExecutionPolicy", "Bypass",
            "-File", wrapperPath,
            scriptPath
        };
    }
}
