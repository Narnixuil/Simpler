using Simpler.Core.Models;

namespace Simpler.Core.Runners;

public interface IScriptRunner
{
    IReadOnlyList<string> SupportedExtensions { get; }

    /// Returns true if the script file has a valid run() entry point.
    bool HasEntryPoint(string scriptPath);

    /// Execute the script with the given context.
    /// MUST NOT throw. Set context.NotifyMessage on failure.
    Task RunAsync(string scriptPath, ScriptContext context);
}
