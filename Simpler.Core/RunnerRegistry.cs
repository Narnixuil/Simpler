using Simpler.Core.Runners;

namespace Simpler.Core;

public class RunnerRegistry
{
    private readonly List<IScriptRunner> _runners;

    public RunnerRegistry()
    {
        _runners = new List<IScriptRunner>
        {
            new JsonRunner()
        };
    }

    public IScriptRunner? GetRunner(string extension)
    {
        string ext = extension.ToLowerInvariant();
        return _runners.FirstOrDefault(r =>
            r.SupportedExtensions.Contains(ext));
    }

    public IEnumerable<string> AllSupportedExtensions =>
        _runners.SelectMany(r => r.SupportedExtensions);
}
