using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace CodexPresence;

public partial class MainWindow
{
    private async Task VerifyInterfaceRecovery(List<string> checks)
    {
        var failures = new List<string>();
        var original = webInterface!;
        HandleWebProcessFailure(CoreWebView2ProcessFailedKind.GpuProcessExited);
        if (!webReady || original.Visibility != Visibility.Visible || NativeViews.Visibility != Visibility.Collapsed) failures.Add("GPU recovery does not replace the modern interface with the native fallback");
        webReady = true; original.Visibility = Visibility.Visible; SetNativeViewAccess(false);
        Navigate("Layout");
        var draftInput = Elements<TextBox>(PageBody).First();
        draftInput.Text = "Unsent recovery draft";
        var savedDraft = draft;
        var snapshot = backend.Snapshot;
        var watch = Stopwatch.StartNew();
        for (var index = 0; index < 30; index++) BuildWebState().ToJsonString();
        var snapshotMilliseconds = watch.Elapsed.TotalMilliseconds / 30;
        var recoveryWatch = Stopwatch.StartNew();
        try { await original.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.crash", "{}").WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException or System.Runtime.InteropServices.COMException) { }
        for (var index = 0; index < 120 && (!webReady || ReferenceEquals(original, webInterface)); index++) await Task.Delay(100);
        if (!webReady || ReferenceEquals(original, webInterface)) failures.Add("A crashed renderer automatically restores the modern interface");
        else
        {
            if (!ReferenceEquals(savedDraft, draft) || !dirty || page != "Layout" || draftInput.Text != "Unsent recovery draft") failures.Add("Recovery preserves the current settings draft and tab");
            Hide(); ShowDashboard(); Navigate("Overview"); SendWebState(); await Task.Delay(200);
            var result = await webInterface!.CoreWebView2.ExecuteScriptAsync("current.page === 'Overview' && document.querySelector('#navigation') !== null");
            if (result != "true" || NativeViews.Visibility != Visibility.Collapsed) failures.Add("Reopening and returning to Overview retains the accepted design");
        }
        if (!backend.Running || backend.Snapshot == null || snapshot == null) failures.Add("Interface recovery keeps the presence engine running");
        dirty = false; editors.Clear();
        File.WriteAllText(Path.Combine(root,"Data","Verification","Web","recovery.json"), JsonSerializer.Serialize(new { failures, snapshotMilliseconds, recoveryMilliseconds = recoveryWatch.Elapsed.TotalMilliseconds }, Backend.JsonOptions));
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
        checks.Add("GPU events, real renderer crashes, drafts, engine continuity and Overview reopening recover correctly");
    }
}
