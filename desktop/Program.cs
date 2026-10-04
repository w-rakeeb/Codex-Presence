using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace CodexPresence;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var testing = Array.IndexOf(args, "--self-test") >= 0 || Array.IndexOf(args, "--interactive-test") >= 0;
        using var mutex = new Mutex(true, testing ? "Local\\CodexPresenceDesktopVerification" : "Local\\CodexPresenceDesktop", out var owner);
        var exiting = Array.IndexOf(args, "--exit") >= 0;
        if (exiting && owner) return 0;
        if (!owner && !testing)
        {
            if (!exiting && (Array.IndexOf(args, "--startup") >= 0 || Array.IndexOf(args, "--watch") >= 0)) return 0;
            try { using var signal = EventWaitHandle.OpenExisting(exiting ? "Local\\CodexPresenceExit" : "Local\\CodexPresenceShow"); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("Codex Presence is already open. Use its tray icon to show the window.", "Codex Presence"); }
            return 0;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.Message, "Codex Presence", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        var window = new MainWindow(args);
        using var showSignal = testing ? null : new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CodexPresenceShow");
        var showRegistration = showSignal == null ? null : ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => app.Dispatcher.BeginInvoke(window.ShowDashboard), null, Timeout.Infinite, false);
        using var exitSignal = testing ? null : new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CodexPresenceExit");
        var exitRegistration = exitSignal == null ? null : ThreadPool.RegisterWaitForSingleObject(exitSignal, (_, _) => app.Dispatcher.BeginInvoke(window.RequestExit), null, Timeout.Infinite, false);
        app.MainWindow = window;
        app.Dispatcher.BeginInvoke(async () => await window.InitializeApp());
        app.Run();
        showRegistration?.Unregister(null);
        exitRegistration?.Unregister(null);
        return window.ExitCode;
    }
}
