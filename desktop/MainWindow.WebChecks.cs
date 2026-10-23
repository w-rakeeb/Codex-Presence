using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace CodexPresence;

public partial class MainWindow
{
    private async Task VerifyWebInterface()
    {
        var evidence = Path.Combine(root, "Data", "Verification", "Web"); Directory.CreateDirectory(evidence);
        var checks = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("Web interface: " + name); checks.Add(name); }
        async Task<string> Script(string code) => await webInterface!.CoreWebView2.ExecuteScriptAsync(code);
        async Task Wait(Func<Task<bool>> ready)
        {
            for (var index = 0; index < 100; index++) { SendWebState(); await Task.Delay(100); if (await ready()) return; }
            throw new TimeoutException("The browser interface did not settle.");
        }
        async Task Page(string target)
        {
            Navigate(target); SendWebState();
            await Wait(async () => await Script($"current?.page === {JsonSerializer.Serialize(target)} && !!document.querySelector('h1')") == "true");
        }
        async Task Capture(string name)
        {
            await Task.Delay(150);
            using var file = File.Create(Path.Combine(evidence, name + ".png"));
            await webInterface!.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
        }
        try
        {
            busy = false; UpdateActionAvailability();
            Width = 1100; Height = 860; ShowDashboard(); await StartEngine();
            await Wait(async () => backend.Snapshot != null && await Script("typeof current === 'object' && current !== null") == "true");
            Check(backend.Options.MonitorOnly && B(backend.Snapshot?["monitoring_only"]), "Browser verification cannot publish Discord activity");
            var preview = backend.Snapshot!["preview"]!.ToJsonString();
            var configBefore = File.ReadAllText(backend.ConfigPath);
            await VerifyInterfaceRecovery(checks);
            await VerifyResponsiveInteractions(checks);
            foreach (var target in navigation.Keys)
            {
                await Page(target); await Capture(target);
                Check(await Script("document.querySelectorAll('#navigation button').length === 6 && !document.querySelector('#page').textContent.includes('[object Object]')") == "true", target + " renders all navigation and native content");
                if (target != "Overview") Check(await Script("document.querySelectorAll('[data-control]').length") == webControls.Values.Count(element => element is TextBox or CheckBox or ComboBox).ToString(), target + " exposes every native form control");
                Check(await Script("document.documentElement.scrollWidth <= innerWidth && document.body.scrollHeight <= innerHeight") == "true", target + " has no window overflow");
            }
            Check(backend.Snapshot!["preview"]!.ToJsonString() == preview && File.ReadAllText(backend.ConfigPath) == configBefore, "Browsing five tabs preserves exact engine preview and configuration");
            await Page("Privacy");
            var model = Elements<CheckBox>(PageBody).Single(box => AutomationProperties.GetName(box) == "Model"); var beforeModel = model.IsChecked;
            await Script("{const input = document.querySelector('input[aria-label=\"Model\"]'); input.checked = !input.checked; input.dispatchEvent(new Event('change', {bubbles:true}));}");
            await Wait(() => Task.FromResult(dirty && model.IsChecked != beforeModel));
            Check(File.ReadAllText(backend.ConfigPath) == configBefore, "Browser edits remain drafts until Save");
            await Page("Layout"); await Page("Privacy");
            Check(Elements<CheckBox>(PageBody).Single(box => AutomationProperties.GetName(box) == "Model").IsChecked != beforeModel, "Browser navigation retains unsaved privacy edits");
            await Script("document.querySelector('[data-command=save]').click()");
            await Wait(() => Task.FromResult(!busy && !dirty));
            Check(File.ReadAllText(backend.ConfigPath) != configBefore, "Browser Save reaches existing atomic configuration writer");
            await Page("Layout");
            Check(await Script("document.querySelectorAll('.preview-rail [data-preview]').length === 3") == "true", "Editor preview exposes all three exact engine text strings");
            var previewMatches = false;
            await Wait(async () =>
            {
                var expectedPreview = new[] { "app_name", "details", "state" }.Select(key => S(backend.Snapshot?["preview"]?[key], "")).ToArray();
                previewMatches = JsonSerializer.Deserialize<string[]>(await Script("[...document.querySelectorAll('.preview-rail [data-preview]')].map(item => item.textContent)"))!.SequenceEqual(expectedPreview);
                return previewMatches;
            });
            Check(previewMatches, "Browser preview text matches the engine byte for byte");
            await Script("{const input = document.querySelector('input[aria-label=\"Custom text\"]'); input.focus(); input.value = 'Focus check'; input.dispatchEvent(new Event('input', {bubbles:true})); input.setSelectionRange(4,4); window.focusProbe = input;}");
            await Task.Delay(1200);
            Check(await Script("document.activeElement === window.focusProbe && window.focusProbe.selectionStart === 4 && window.focusProbe.value === 'Focus check'") == "true", "Live updates preserve the focused input, text and caret");
            await Page("Settings");
            foreach (var section in new[] { "Appearance", "Activity", "Background", "Advanced" })
            {
                await Script($"document.querySelector('[data-section={section}]').click()");
                await Wait(async () => settingsSection == section && await Script($"current.section === '{section}'") == "true");
                Check(await Script("document.querySelectorAll('[data-control]').length") == webControls.Values.Count(element => element is TextBox or CheckBox or ComboBox).ToString(), section + " exposes every lazy settings control");
                await Capture("Settings-" + section);
            }
            ShowSettingsSection("Appearance"); SendWebState(); await Task.Delay(200);
            await Script("{const select = document.querySelector('select[aria-label=\"Color mode\"]'); select.selectedIndex = 2; select.dispatchEvent(new Event('change', {bubbles:true}));}");
            await Wait(async () => await Script("document.documentElement.dataset.theme === 'nord'") == "true");
            await Capture("Settings-Light");
            Check(backend.Options.ColorMode == "dark", "Browser appearance preview does not save preferences early");
            await Script("{const select = document.querySelector('select[aria-label=\"Interface style\"]'); select.selectedIndex = 1; select.dispatchEvent(new Event('change', {bubbles:true}));}");
            await Wait(async () => await Script("document.documentElement.dataset.style === 'soft'") == "true");
            await Capture("Settings-Soft-Light");
            Check(await Script("getComputedStyle(document.querySelector('h1')).fontFamily.includes('Georgia')") == "true", "Soft style changes typography without changing Discord data");
            await Script("{const select = document.querySelector('select[aria-label=\"Color mode\"]'); select.selectedIndex = 1; select.dispatchEvent(new Event('change', {bubbles:true}));}");
            await Task.Delay(200);
            await Page("Overview");
            foreach (var state in new[] { "live", "paused", "stopped", "error", "waiting", "loading", "idle" })
            {
                visualStateOverride = state; SendWebState(); await Task.Delay(200); await Capture("State-" + state);
                Check(await Script($"document.querySelector('.engine-card h2').textContent === {JsonSerializer.Serialize(EnginePresentation(backend.Snapshot).Headline)}") == "true", state + " has the correct browser engine state");
            }
            visualStateOverride = null;
            foreach (var size in new[] { (560d, 640d), (1100d, 860d), (1400d, 960d) })
            {
                Width = size.Item1; Height = size.Item2;
                foreach (var target in navigation.Keys)
                {
                    await Page(target); await Task.Delay(150); await Capture(target + "-" + size.Item1);
                    Check(await Script("document.documentElement.scrollWidth <= innerWidth && document.body.scrollHeight <= innerHeight") == "true", target + " fits browser window width " + size.Item1);
                }
            }
            await Page("Overview");
            var widths = new List<double>();
            foreach (var width in new[] { 1100d, 1500d, 1750d })
            {
                Width = width; await Task.Delay(250);
                widths.Add(JsonSerializer.Deserialize<double>(await Script("document.querySelector('#engine-region > .card').getBoundingClientRect().width")));
                Check(await Script("Math.abs(document.querySelector('.page-content').getBoundingClientRect().width - document.querySelector('.stats').getBoundingClientRect().width) < 2") == "true", "Statistics stretch with content at width " + width);
                await Script("document.querySelector('#engine-more').click()"); await Task.Delay(150);
                await Capture("Menu-" + width);
                var menuStatus = await Script("(() => { const menu=document.querySelector('#engine-actions'), r=menu.getBoundingClientRect(); return { open:menu.matches(':popover-open'), left:r.left, top:r.top, width:r.width, bottom:r.bottom, viewport:innerHeight, hit:document.elementFromPoint(r.left+20,r.top+20)?.outerHTML?.slice(0,150) }; })()");
                Check(await Script("(() => { const menu=document.querySelector('#engine-actions'), r=menu.getBoundingClientRect(); return menu.matches(':popover-open') && r.top >= 0 && r.bottom <= innerHeight && document.elementFromPoint(r.left+20,r.top+20)?.closest('#engine-actions') !== null; })()") == "true", "Overflow menu is visible above cards at width " + width + " " + menuStatus);
                await Script("document.querySelector('#engine-actions').hidePopover()");
            }
            Check(widths[1] > widths[0] + 100 && widths[2] > widths[1] + 50, "Cards keep growing past the previous fixed content width");
            Check(NativeViews.Visibility == Visibility.Collapsed && webInterface!.Margin.Left >= 8 && webInterface.Margin.Bottom >= 8, "Hidden native layout is collapsed and the browser leaves a native resize gutter");
            var handled = false;
            ResizeWindowMessage(IntPtr.Zero, 0x0231, IntPtr.Zero, IntPtr.Zero, ref handled);
            await Task.Delay(150);
            Check(windowResizing && !refreshTimer.IsEnabled && await Script("document.documentElement.hasAttribute('data-resizing')") == "true", "Interactive resizing defers snapshot layout and disables motion");
            ResizeWindowMessage(IntPtr.Zero, 0x0232, IntPtr.Zero, IntPtr.Zero, ref handled);
            await Task.Delay(150);
            Check(!windowResizing && await Script("!document.documentElement.hasAttribute('data-resizing')") == "true", "Resize completion restores live interface updates");
            WindowState = WindowState.Maximized; await Task.Delay(500);
            var workArea = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
            var browserBottom = webInterface!.PointToScreen(new Point(0, webInterface.ActualHeight));
            Check(browserBottom.Y <= workArea.Bottom + 1, "Maximized browser and footer stay above the taskbar");
            WindowState = WindowState.Normal; Width = 1100; Height = 860; await Task.Delay(200);
            await Page("Manual");
            Check(await Script("document.querySelector('h1').textContent === 'Manual presence' && document.querySelector('select[aria-label=\"Timestamp option\"]').options.length === 5") == "true", "Manual presence is a separate section with all five timeline options");
            await backend.Stop();
            var idleFixture = Directory.GetFiles(Path.Combine(backend.Options.CodexHome, "sessions"), "fixture.jsonl", SearchOption.AllDirectories).Single();
            File.AppendAllText(idleFixture, JsonSerializer.Serialize(new { type = "response_item", timestamp = DateTimeOffset.UtcNow.ToString("O"), payload = new { type = "message", role = "assistant", phase = "final_answer" } }) + Environment.NewLine);
            backend.WaitForNewWork = true; await backend.Start();
            Check(B(backend.Snapshot?["startup_waiting"]), "Automatic startup remains gated before a new real Codex prompt");
            var automaticPreview = backend.Snapshot!["preview"]!.ToJsonString();
            await Script("{ const field=document.querySelector('input[aria-label=\"Activity and project\"]'); field.value='Thinking · Manual fixture'; field.dispatchEvent(new Event('input',{bubbles:true})); const second=document.querySelector('input[aria-label=\"Second line (optional)\"]'); second.value='Model · High'; second.dispatchEvent(new Event('input',{bubbles:true})); }");
            await Wait(() => Task.FromResult(manualProfileUnsaved));
            Check(await Script("current.dirtyTabs.includes('Manual')") == "true", "Manual text edits show an unsaved marker without publishing");
            foreach (var mode in new[] { "session", "connection", "update", "local", "custom" })
            {
                var modes = new[] { "connection", "update", "session", "local", "custom" };
                await Script($"{{ const select=document.querySelector('select[aria-label=\"Timestamp option\"]'); select.selectedIndex={Array.IndexOf(modes, mode)}; select.dispatchEvent(new Event('change',{{bubbles:true}})); }}");
                await Wait(() => Task.FromResult(S(LoadManualProfile()["timestamp_mode"]) == mode));
                if (mode == "custom")
                {
                    var start = JsonSerializer.Serialize(DateTime.Now.AddHours(-1).ToString("yyyy-MM-ddTHH:mm"));
                    var end = JsonSerializer.Serialize(DateTime.Now.AddHours(1).ToString("yyyy-MM-ddTHH:mm"));
                    await Script($"{{ const start=document.querySelector('input[aria-label=\"Start (local date and time)\"]'); start.value={start}; start.dispatchEvent(new Event('input',{{bubbles:true}})); const end=document.querySelector('input[aria-label=\"End (optional local date and time)\"]'); end.value={end}; end.dispatchEvent(new Event('input',{{bubbles:true}})); }}");
                }
                var revision = N(backend.Snapshot?["manual_revision"]);
                await Script("[...document.querySelectorAll('button[data-action]')].find(button=>button.textContent==='Start or apply manual presence').click()");
                await Wait(() => Task.FromResult(!busy && N(backend.Snapshot?["manual_revision"]) > revision));
                Check(!manualProfileUnsaved && File.ReadAllText(ManualProfilePath).Contains("Manual fixture"), mode + " applies and saves the profile through the real browser action");
                if (mode == "custom") await Capture("Manual-Custom-Active");
                Check(B(backend.Snapshot?["manual_mode"]) && S(backend.Snapshot?["preview"]?["details"]) == "Thinking · Manual fixture", mode + " uses the explicit manual text without altering session metrics");
                Check(File.ReadAllText(backend.ConfigPath) != "" && !File.ReadAllText(backend.ConfigPath).Contains("Manual fixture"), mode + " keeps manual settings out of shared runtime configuration");
                if (mode == "custom") Check(N(backend.Snapshot?["manual_end_epoch"]) > N(backend.Snapshot?["manual_start_epoch"]), "Custom countdown retains distinct start and end timestamps");
            }
            await Script("{const source=document.querySelector('select[aria-label=\"Presence source\"]'); source.selectedIndex=1; source.dispatchEvent(new Event('change',{bubbles:true}));}");
            await Wait(() => Task.FromResult(S(LoadManualProfile()["source"]) == "recent"));
            Check(await Script("document.querySelector('select[aria-label=\"Recent Codex session\"]').options.length > 0") == "true", "Manual mode lists recent Codex chats for selection");
            await Script("{const session=document.querySelector('select[aria-label=\"Recent Codex session\"]'); session.selectedIndex=0; session.dispatchEvent(new Event('change',{bubbles:true}));}");
            var selectedManualId = S(backend.Snapshot?["recent_sessions"]?.AsArray().FirstOrDefault()?["session_id"]);
            var selectedRevision = N(backend.Snapshot?["manual_revision"]);
            await Script("[...document.querySelectorAll('button[data-action]')].find(button=>button.textContent==='Start or apply manual presence').click()");
            await Wait(() => Task.FromResult(!busy && N(backend.Snapshot?["manual_revision"]) > selectedRevision));
            Check(B(backend.Snapshot?["manual_mode"]) && S(backend.Snapshot?["manual_session"]?["session_id"]) == selectedManualId, "Manual mode publishes the explicitly selected recent session");
            Check(backend.Snapshot!["preview"]!.ToJsonString() == automaticPreview, "Recent-session manual preview exactly matches automatic model, effort, activity, usage and artwork");
            Check(N(backend.Snapshot?["manual_end_epoch"]) > N(backend.Snapshot?["manual_start_epoch"]), "Recent-session presence retains the chosen custom countdown");
            Check(S(LoadManualProfile()["details"]) == "Thinking · Manual fixture" && S(LoadManualProfile()["state"]) == "Model · High", "Selecting a recent session preserves the saved custom text");
            await Capture("Manual-Recent-Session");
            Check(backend.WaitForNewWork, "Manual mode does not unlock the real-work startup gate");
            await backend.SetPriorityHold(true);
            await Wait(() => Task.FromResult(B(backend.Snapshot?["priority_held"])));
            Check(B(backend.Snapshot?["manual_mode"]) && !B(backend.Snapshot?["presence_enabled"]), "App priority holds manual presence without losing its selected profile");
            await backend.SetPriorityHold(false);
            await backend.SetManualPresence(null);
            Check(B(backend.Snapshot?["startup_waiting"]), "Returning to automatic still waits for fresh real work");
            Check(!B(backend.Snapshot?["manual_mode"]) && backend.Snapshot!["preview"]!.ToJsonString() == automaticPreview, "Returning to automatic restores the original Discord preview exactly");
            LoadManualProfile()["timestamp_mode"] = "session";
            await ApplyManualPresence(); await backend.Stop(); await backend.Start();
            Check(!B(backend.Snapshot?["manual_mode"]), "Restarting the engine does not automatically resume manual presence");
            backend.WaitForNewWork = false; await backend.Stop(); await backend.Start();
            await Page("Overview");
            Check(await Script("!!document.querySelector('[data-command=background]') && getComputedStyle(document.querySelector('.menu')).display !== 'none'") == "true", "Background and navigation remain available at every window size");
            Width = 1100; Height = 860;
            foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
            {
                webInterface!.ZoomFactor = scale;
                foreach (var target in navigation.Keys)
                {
                    await Page(target); await Task.Delay(100); await Capture(target + "-zoom-" + (scale * 100));
                    Check(await Script("document.documentElement.scrollWidth <= innerWidth && document.querySelector('.page-scroll').scrollWidth <= document.querySelector('.page-scroll').clientWidth + 1") == "true", target + " fits " + scale * 100 + "% browser zoom");
                }
            }
            webInterface!.ZoomFactor = 1;
            await Page("Settings");
            await Script("{const input=document.querySelector('#setting-search'); input.value='no such setting'; input.dispatchEvent(new Event('input',{bubbles:true}));}");
            Check(await Script("!document.querySelector('#search-empty').hidden && document.querySelector('[data-command=save]') !== null") == "true", "Settings search explains an empty result without removing save actions");
            await Script("{const input=document.querySelector('#setting-search'); input.value=''; input.dispatchEvent(new Event('input',{bubbles:true}));}");
            await webInterface.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"features\":[{\"name\":\"forced-colors\",\"value\":\"active\"},{\"name\":\"prefers-reduced-motion\",\"value\":\"reduce\"}]}");
            await Capture("Settings-High-Contrast-Reduced-Motion");
            Check(await Script("matchMedia('(forced-colors: active)').matches && matchMedia('(prefers-reduced-motion: reduce)').matches") == "true", "Browser honors high contrast and reduced motion media");
            await webInterface.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"features\":[]}");
            var initialUri = webInterface!.Source;
            await Script("location.href = 'https://example.com/'"); await Task.Delay(200);
            Check(webInterface.Source == initialUri, "External navigation cannot receive desktop bridge access");
            File.WriteAllText(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new { passed = checks.Count, checks }, Backend.JsonOptions));
            dirty = false; editors.Clear(); closing = true; await backend.Stop(); ExitCode = 0; Close(); Application.Current.Shutdown();
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(evidence, "failure.txt"), error.ToString());
            try { await backend.Stop(); } catch { }
            ExitCode = 1; closing = true; Close(); Application.Current.Shutdown(1);
        }
    }
}
