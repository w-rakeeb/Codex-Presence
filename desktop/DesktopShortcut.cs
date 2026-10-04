using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CodexPresence;

public static class DesktopShortcut
{
    public static string Create(string root, string? directory = null)
    {
        var folder = directory ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Codex Presence.lnk");
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic link = shell.CreateShortcut(path);
        try
        {
            var target = Path.Combine(root, "Codex Presence.exe");
            if (File.Exists(path) && !string.Equals((string)link.TargetPath, target, StringComparison.OrdinalIgnoreCase))
                throw new IOException("A different Codex Presence shortcut already exists on this desktop.");
            link.TargetPath = target;
            link.WorkingDirectory = root;
            link.Arguments = "";
            link.Description = "Open the Codex Presence dashboard";
            link.IconLocation = Path.Combine(root, "Runtime", "app.ico");
            link.Save();
            return path;
        }
        finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
    }
}
