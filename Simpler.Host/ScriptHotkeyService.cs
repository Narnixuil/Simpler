using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Input;
using Simpler.Core.Models;

namespace Simpler.Host;

public class ScriptHotkeyService
{
    private static readonly Dictionary<string, GlobalHotkey> ScriptHotkeys =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (ModifierKeys Mods, Key Key)> ScriptHotkeyDefs =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> HotkeyOwners =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _scriptsDir;
    private readonly Action<string> _showNotification;

    public ScriptHotkeyService(string scriptsDir, Action<string> showNotification)
    {
        _scriptsDir = scriptsDir;
        _showNotification = showNotification;
    }

    public bool EnsureHotkeyActive(ScriptMeta script)
    {
        if (ScriptHotkeyDefs.ContainsKey(script.Path))
            return true;

        if (!TryLoadHotkeyFile(script, out var mods, out var key))
            return false;

        if (TryRegisterScriptHotkey(script, mods, key, out _))
            return true;

        DeleteHotkeyFile(script);
        return false;
    }

    public bool TryRegisterScriptHotkey(ScriptMeta script, ModifierKeys mods, Key key, out string? error)
    {
        error = null;
        string signature = BuildHotkeySignature(mods, key);

        if (HotkeyOwners.TryGetValue(signature, out var owner) &&
            !owner.Equals(script.Path, StringComparison.OrdinalIgnoreCase))
        {
            error = "Hotkey is already in use";
            return false;
        }

        UnregisterScriptHotkey(script);

        var hotkey = new GlobalHotkey(mods, key,
            () => App.RunScriptByPath(script.Path));

        if (!hotkey.Register())
        {
            error = "Failed to register hotkey (possibly in use)";
            return false;
        }

        ScriptHotkeys[script.Path] = hotkey;
        ScriptHotkeyDefs[script.Path] = (mods, key);
        HotkeyOwners[signature] = script.Path;
        return true;
    }

    public void UnregisterScriptHotkey(ScriptMeta script)
    {
        if (ScriptHotkeyDefs.TryGetValue(script.Path, out var def))
        {
            var signature = BuildHotkeySignature(def.Mods, def.Key);
            HotkeyOwners.Remove(signature);
            ScriptHotkeyDefs.Remove(script.Path);
        }

        if (!ScriptHotkeys.TryGetValue(script.Path, out var hotkey)) return;
        hotkey.Unregister();
        ScriptHotkeys.Remove(script.Path);
    }

    public void SaveHotkeyFile(ScriptMeta script, ModifierKeys mods, Key key)
    {
        try
        {
            string path = GetHotkeySettingsPath(script);
            File.WriteAllText(path, $"{(int)mods}|{key}");
        }
        catch (Exception ex)
        {
            _showNotification("Failed to save hotkey: " + ex.Message);
        }
    }

    public void DeleteHotkeyFile(ScriptMeta script)
    {
        try
        {
            string path = GetHotkeySettingsPath(script);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    public void CleanupHotkeysOnExit()
    {
        foreach (var hotkey in ScriptHotkeys.Values)
            hotkey.Unregister();

        ScriptHotkeys.Clear();
        ScriptHotkeyDefs.Clear();
        HotkeyOwners.Clear();

        try
        {
            if (!Directory.Exists(_scriptsDir)) return;

            foreach (var file in Directory.EnumerateFiles(
                         _scriptsDir, "_*.hotkey.txt", SearchOption.AllDirectories))
            {
                try { File.Delete(file); }
                catch { }
            }
        }
        catch { }
    }

    public static string GetScriptDisplayName(ScriptMeta script)
    {
        if (!string.IsNullOrWhiteSpace(script.Name))
            return script.Name;
        if (!string.IsNullOrWhiteSpace(script.FileName))
            return script.FileName;
        return Path.GetFileName(script.Path);
    }

    public static string FormatHotkey(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private static string BuildHotkeySignature(ModifierKeys mods, Key key)
        => $"{(int)mods}:{key}";

    private bool TryLoadHotkeyFile(ScriptMeta script, out ModifierKeys mods, out Key key)
    {
        mods = ModifierKeys.None;
        key = Key.None;

        try
        {
            string path = GetHotkeySettingsPath(script);
            if (!File.Exists(path)) return false;

            string text = (File.ReadAllText(path) ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split('|');
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], out var modsValue)) return false;
            if (!Enum.TryParse(parts[1], out Key parsedKey)) return false;

            mods = (ModifierKeys)modsValue;
            key = parsedKey;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string GetHotkeySettingsPath(ScriptMeta script)
    {
        string dir = Path.GetDirectoryName(script.Path) ?? _scriptsDir;
        string name = Path.GetFileNameWithoutExtension(script.Path);
        return Path.Combine(dir, $"_{name}.hotkey.txt");
    }
}
