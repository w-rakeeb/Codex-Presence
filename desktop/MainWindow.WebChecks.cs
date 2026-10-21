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
            foreach (var target in navigation.Keys)
            {
                await Page(target); await Capture(target);
                Check(await Script("document.querySelectorAll('#navigation button').length === 5 && !document.querySelector('#page').textContent.includes('[object Object]')") == "true", target + " renders all navigation and native content");
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
            var expectedPreview = new[] { "app_name", "details", "state" }.Select(key => S(backend.Snapshot?["preview"]?[key], "")).ToArray();
            Check(JsonSerializer.Deserialize<string[]>(await Script("[...document.querySelectorAll('.preview-rail [data-preview]')].map(item => item.textContent)"))!.SequenceEqual(expectedPreview), "Browser preview text matches the engine byte for byte");
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
