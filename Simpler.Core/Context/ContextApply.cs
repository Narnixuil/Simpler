using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Text;
using Simpler.Core.Models;

namespace Simpler.Core.Context;

public static class ContextApply
{
    public static Task CommitAsync(ScriptContext context)
    {
        return Task.Run(() => StaRunner.Run(() => CommitImpl(context)));
    }

    private static void CommitImpl(ScriptContext context)
    {
        // ©¤©¤ 1. Paste output text ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        if (!string.IsNullOrEmpty(context.OutputText))
        {
            try
            {
                Clipboard.SetText(context.OutputText);
                System.Threading.Thread.Sleep(50);
                SendKeys.SendWait("^v");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ContextApply paste error: {ex.Message}");
            }
        }

        // ©¤©¤ 2. Apply renames ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        foreach (var kvp in context.RenameMap)
        {
            string oldPath = kvp.Key;
            string newName = kvp.Value;
            try
            {
                string dir = Path.GetDirectoryName(oldPath)
                    ?? throw new InvalidOperationException(
                           "Cannot determine directory");
                string newPath = Path.Combine(dir, newName);

                bool isFile = File.Exists(oldPath);
                bool isDir = Directory.Exists(oldPath);

                if (!isFile && !isDir)
                {
                    Debug.WriteLine(
                        $"Rename skipped (source not found): {oldPath}");
                    continue;
                }
                if (File.Exists(newPath) || Directory.Exists(newPath))
                {
                    Debug.WriteLine(
                        $"Rename skipped (target exists): {newPath}");
                    continue;
                }

                if (isDir)
                    Directory.Move(oldPath, newPath);
                else
                    File.Move(oldPath, newPath);

                Debug.WriteLine($"Renamed: {oldPath} ¡ú {newPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Rename failed [{oldPath}]: {ex.Message}");
            }
        }
    }
}
