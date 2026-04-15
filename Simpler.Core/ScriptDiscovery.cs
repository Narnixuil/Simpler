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
            if (TryDiscoverFile(filePath, registry, out var meta))
                results.Add(meta);
        }

        return results;
    }

    public static bool TryDiscoverFile(string filePath, RunnerRegistry registry, out ScriptMeta meta)
    {
        meta = new ScriptMeta();

        string fileName = Path.GetFileName(filePath);
        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (ShouldSkip(fileName, ext, registry))
            return false;

        meta = new ScriptMeta
        {
            Path = filePath,
            FileName = fileName,
            Lang = LangNames.GetValueOrDefault(ext, ext.TrimStart('.')),
            Icon = DefaultIcons.GetValueOrDefault(ext, "🔧"),
            Name = Path.GetFileNameWithoutExtension(fileName),
        };

        if (ext == ".json")
        {
            if (TryLoadJsonMeta(filePath, meta, out var hasRunFromJson, out var parseError))
            {
                meta.HasRun = hasRunFromJson;
            }
            else
            {
                meta.HasRun = false;
                meta.DisabledReason = string.IsNullOrWhiteSpace(parseError)
                    ? "Invalid JSON script."
                    : $"Invalid JSON script: {parseError}";
            }
        }
        else
        {
            var runner = registry.GetRunner(ext);
            if (runner != null)
            {
                try { meta.HasRun = runner.HasEntryPoint(filePath); }
                catch { meta.HasRun = false; }
            }
        }

        if (!meta.HasRun && string.IsNullOrWhiteSpace(meta.DisabledReason))
            meta.DisabledReason = $"No run() entry point found in {fileName}";

        return true;
    }

    private static bool ShouldSkip(string fileName, string ext, RunnerRegistry registry)
    {
        if (fileName.StartsWith("_")) return true;
        if (fileName.Equals("Template.json", StringComparison.OrdinalIgnoreCase)) return true;
        if (fileName.EndsWith(".meta.json")) return true;
        if (!registry.AllSupportedExtensions.Contains(ext)) return true;
        return false;
    }

    private static bool TryLoadJsonMeta(string path, ScriptMeta meta, out bool hasRun, out string? parseError)
    {
        hasRun = false;
        parseError = null;
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

            hasRun = root.TryGetProperty("steps", out var steps) &&
                     steps.ValueKind == System.Text.Json.JsonValueKind.Array;
            return true;
        }
        catch (Exception ex)
        {
            parseError = ex.Message;
            return false;
        }
    }
}

