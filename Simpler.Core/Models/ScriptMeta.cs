namespace Simpler.Core.Models;

public class ScriptMeta
{
    public string Path { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "🔧";
    public string Lang { get; set; } = "";
    public bool HasRun { get; set; }
    public string? DisabledReason { get; set; }
}
