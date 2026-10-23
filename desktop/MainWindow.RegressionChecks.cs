using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;

namespace CodexPresence;

public partial class MainWindow
{
    private async Task VerifyResponsiveInteractions(List<string> checks)
    {
        var failures = new List<string>();
        void Verify(bool result, string label) { if (result) checks.Add(label); else failures.Add(label); }
        async Task<string> Script(string code) => await webInterface!.CoreWebView2.ExecuteScriptAsync(code);
        var interactionFailures = await Script("""
            (() => {
              const original = structuredClone(current), failures = [];
              try {
                const state = structuredClone(original);
                const focus = document.querySelector('#engine-more'); focus.focus();
                state.snapshot.preview.details += ' · updated'; window.receive(state);
                if (document.activeElement !== focus) failures.push('Live updates preserve the focused engine control');
                state.snapshot.metrics.totals.total_tokens = 1234567890;
                const active = state.snapshot.sessions.find(session => session.session_id === state.snapshot.active_session_id);
                active.context_window.remaining_percent = 10; window.receive(state);
                if (document.querySelector('[data-stat=token] .stat-value').title !== (1234567890).toLocaleString()) failures.push('Token tooltips track the current full value');
                if (!document.querySelector('[data-stat=context] progress').classList.contains('progress-warning')) failures.push('Context warning updates when usage crosses 80 percent');
                state.decision = [{id:'regression-confirm',kind:'button',label:'Keep editing',text:'Keep editing'}];
                window.receive(state);
                const confirm = document.querySelector('[data-action=regression-confirm]'); confirm.focus(); window.receive(state);
                if (document.activeElement !== confirm) failures.push('Confirmation focus survives live status updates');
                window.receive(original);
                for (let index=0;index<5;index++) toast('Profile saved.');
                if (document.querySelector('#toasts').children.length > 1) failures.push('Repeated feedback stays in one unobtrusive toast');
              } finally { window.receive(original); document.querySelector('#toasts').replaceChildren(); }
              return failures;
            })()
            """);
        var labels = JsonSerializer.Deserialize<string[]>(interactionFailures) ?? Array.Empty<string>();
        failures.AddRange(labels);
        if (labels.Length == 0) checks.Add("Live updates preserve control/dialog focus and refresh telemetry and feedback");
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
        {
            WindowState = WindowState.Normal; Width = 560; Height = 720;
            webInterface!.ZoomFactor = scale;
            foreach (var target in new[] { "Overview", "Layout", "Settings", "Manual" })
            {
                Navigate(target); SendWebState(); await Task.Delay(200);
                var clipped = await Script("""
                    (() => {
                      const page = document.querySelector('#page').getBoundingClientRect();
                      return [...document.querySelectorAll('#page button,#page input,#page select,#page textarea,#page summary,#page .stat-title,#page .stat-value,#page .badge,#page .card-title')].filter(element => {
                        const r = element.getBoundingClientRect();
                        return r.width > 0 && r.height > 0 && (r.left < page.left - 2 || r.right > page.right + 2 || element.matches('button,.stat-title,.stat-value') && element.scrollWidth > element.clientWidth + 2);
                      }).map(element => element.getAttribute('aria-label') || element.textContent.trim()).slice(0,8);
                    })()
                    """);
                Verify(clipped == "[]", target + " controls and metrics fit the minimum window at " + scale * 100 + "% zoom: " + clipped);
                Verify(await Script("(() => { const sidebar=document.querySelector('.app-sidebar'); return sidebar.scrollHeight <= sidebar.clientHeight + 1 || ['auto','scroll'].includes(getComputedStyle(sidebar).overflowY); })()") == "true", target + " navigation remains reachable at " + scale * 100 + "% zoom");
                if (scale == 2)
                {
                    var path = Path.Combine(root, "Data", "Verification", "Web", target + "-minimum-200.png");
                    using var file = File.Create(path);
                    await webInterface.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, file);
                }
            }
        }
        webInterface!.ZoomFactor = 1; Width = 1100; Height = 860;
        WindowState = WindowState.Maximized; UpdateResponsiveLayout(); await Task.Delay(150);
        Verify(AutomationProperties.GetName(MaximizeButton) == "Restore window", "Maximized browser window exposes the Restore caption action");
        WindowState = WindowState.Normal; UpdateResponsiveLayout(); Navigate("Overview"); SendWebState();
        File.WriteAllText(Path.Combine(root, "Data", "Verification", "Web", "interaction-regressions.json"), JsonSerializer.Serialize(new { failures }, Backend.JsonOptions));
        if (failures.Count > 0) throw new InvalidOperationException("Interaction regressions: " + string.Join("; ", failures));
    }
}
