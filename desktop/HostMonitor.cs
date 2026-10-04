using System;
using System.Diagnostics;

namespace CodexPresence;

public static class HostMonitor
{
    public static bool IsRunning()
    {
        foreach (var name in new[] { "ChatGPT", "Codex" })
        {
            var processes = Process.GetProcessesByName(name);
            var running = processes.Length > 0;
            foreach (var process in processes) process.Dispose();
            if (running) return true;
        }
        return false;
    }
}
