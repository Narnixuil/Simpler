using Simpler.Core.Models;

namespace Simpler.Core;

public static class ScriptDiscovery
{
    private static readonly Dictionary<string, string> DefaultIcons = new()
    {
        { ".json", "🧩" }
    };

    private static readonly Dictionary<string, string> LangNames = new()
    {
        { ".json", "JSON" }
    };

    public static List<ScriptMeta> Discover(
        string scriptsDir, RunnerRegistry registry)
    {
        var results = new List<ScriptMeta>();
        if (!Directory.Exists(scriptsDir)) return results;

        foreach (var filePath in
            Directory.GetFiles(scriptsDir).OrderBy(f => f))
        {
            string fileName = Path.GetFileName(filePath);
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            // Skip hidden, meta, and unsupported files.
            if (fileName.StartsWith("_")) continue;
            if (fileName.Equals("Template.json", StringComparison.OrdinalIgnoreCase)) continue;
            if (fileName.EndsWith(".meta.json")) continue;
            if (!registry.AllSupportedExtensions.Contains(ext)) continue;

            var meta = new ScriptMeta
            {
                Path     = filePath,
                FileName = fileName,
                Lang     = LangNames.GetValueOrDefault(ext, ext.TrimStart('.')),
                Icon     = DefaultIcons.GetValueOrDefault(ext, "🔧"),
                Name     = Path.GetFileNameWithoutExtension(fileName),
            };

            if (ext == ".json")
            {
                TryLoadJsonMeta(filePath, meta);
            }

            // Check entry point.
            var runner = registry.GetRunner(ext);
            if (runner != null)
            {
                try   { meta.HasRun = runner.HasEntryPoint(filePath); }
                catch { meta.HasRun = false; }
            }

            if (!meta.HasRun)
                meta.DisabledReason =
                    $"No run() entry point found in {fileName}";

            results.Add(meta);
        }

        return results;
    }

    private static void TryLoadJsonMeta(string path, ScriptMeta meta)
    {
        try
        {
            string text = File.ReadAllText(path);
            var doc = System.Text.Json.JsonDocument.Parse(text);
            var root = doc.RootElement;

            string? name = root.TryGetProperty("name", out var n)
                ? n.GetString()
                : null;
            string? desc = root.TryGetProperty("description", out var d)
                ? d.GetString()
                : null;
            string? icon = root.TryGetProperty("icon", out var i)
                ? i.GetString()
                : null;

            if (!string.IsNullOrWhiteSpace(name)) meta.Name = name!;
            if (!string.IsNullOrWhiteSpace(desc)) meta.Description = desc!;

            if (!string.IsNullOrWhiteSpace(icon))
            {
                if (icon.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                    icon.StartsWith("fa:", StringComparison.OrdinalIgnoreCase))
                    meta.Icon = "🧩";
                else
                    meta.Icon = icon;
            }
        }
        catch { }
    }
}

