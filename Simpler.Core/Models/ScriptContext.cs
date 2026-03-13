namespace Simpler.Core.Models;

public class ScriptContext
{
    // ── Inputs (populated by ContextCapture) ──────────────
    public string SelectedText { get; set; } = "";
    public List<string> SelectedFiles { get; set; } = new();
    public string ActiveWindowTitle { get; set; } = "";

    // ── Outputs (written by script, consumed by ContextApply) ──
    public string? OutputText { get; set; }
    public Dictionary<string, string> RenameMap { get; set; } = new();
    // RenameMap: key = original full path, value = new filename only

    public string? NotifyMessage { get; set; }

    // ── Script-callable API ────────────────────────────────
    public string GetSelectedText() => SelectedText;
    public List<string> GetSelectedFiles() => new(SelectedFiles);
    public void SetOutputText(string text) => OutputText = text;
    public void RenameFile(string originalPath, string newName)
        => RenameMap[originalPath] = newName;
    public void Notify(string message) => NotifyMessage = message;
}
