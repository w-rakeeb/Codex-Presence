using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexPresence;

public partial class MainWindow
{
    private async Task AuditChecks(List<string> checks, string evidence)
    {
        void Check(bool result, string label) { if (!result) throw new InvalidOperationException("Verification failed: " + label); checks.Add(label); File.WriteAllText(Path.Combine(evidence, "audit-progress.txt"), label); }
        T Named<T>(string name) where T : DependencyObject => Elements<T>(PageBody).SingleOrDefault(item => AutomationProperties.GetName(item) == name) ?? throw new InvalidOperationException("Missing audit control: " + name);
        void Fresh(string target) { dirty = false; editors.Clear(); draft = null; draftBase = null; Navigate(target); UpdateLayout(); }
        var preferenceFixture = Path.Combine(evidence, "AuditPreferences");
        Directory.CreateDirectory(Path.Combine(preferenceFixture, "Data"));
        var preferencePath = Path.Combine(preferenceFixture, "Data", "app-settings.json");
        var damaged = "{ broken settings";
        File.WriteAllText(preferencePath, damaged);
        var safe = Preferences.Load(preferenceFixture, out var warning);
        Check(warning != null && !safe.StartWithWindows && !safe.StartWithChatGpt && !safe.StartEngineOnOpen && !safe.HideTrayIcon && File.ReadAllText(preferencePath) == damaged, "Damaged preferences use visible safe defaults and preserve the original file");
        File.WriteAllText(preferencePath, "{\"CodexHome\":null}");
        Preferences.Load(preferenceFixture, out warning);
        Check(warning != null && File.ReadAllText(preferencePath).Contains("null"), "Null monitoring paths are reported without overwriting preferences");
        var settings = new Preferences { CodexHome = backend.Options.CodexHome, PollSeconds = 0, StaleSeconds = 999999, StickySeconds = -1 };
        File.WriteAllText(preferencePath, JsonSerializer.Serialize(settings));
        var clamped = Preferences.Load(preferenceFixture, out warning);
        Check(warning != null && clamped.PollSeconds == 1 && clamped.StaleSeconds == 86400 && clamped.StickySeconds == 60, "Out-of-range monitoring preferences recover with supported bounds and a warning");
        var before = File.ReadAllText(preferencePath);
        clamped.Save(preferenceFixture);
        var preferenceBackups = Directory.GetFiles(Path.Combine(preferenceFixture, "Data", "Backups", "Preferences"), "*.json");
        Check(preferenceBackups.Any(path => File.ReadAllText(path) == before), "Preference replacement keeps an exact recoverable backup");
        var stamp = File.GetLastWriteTimeUtc(preferencePath);
        clamped.Save(preferenceFixture);
        Check(File.GetLastWriteTimeUtc(preferencePath) == stamp && Directory.GetFiles(Path.Combine(preferenceFixture, "Data", "Backups", "Preferences"), "*.json").Length == preferenceBackups.Length, "Unchanged desktop preferences avoid disk writes and extra backups");
        var appliedPath = Path.Combine(root, "Data", "Verification", "Preferences", "Data", "app-settings.json");
        var originalPreferences = backend.Options;
        var engineId = backend.ProcessId;
        var failedPreferenceSave = false;
        using (var locked = new FileStream(appliedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { await ApplyPreferences(new Preferences { CodexHome = originalPreferences.CodexHome, PollSeconds = 3, MonitorOnly = true }); }
            catch (IOException) { failedPreferenceSave = true; }
        }
        Check(failedPreferenceSave && ReferenceEquals(backend.Options, originalPreferences) && backend.ProcessId == engineId && backend.Running, "A blocked preference save preserves the live engine and previous monitoring settings");
        Check(ResetTime(JsonValue.Create(long.MaxValue)) == "Unavailable" && ResetTime(JsonValue.Create(long.MinValue)) == "Unavailable", "Malformed quota reset timestamps cannot break the dashboard");
        Check(OriginalDocumentation(evidence).StartsWith("https://github.com/w-rakeeb/", StringComparison.Ordinal), "A portable release opens the published original documentation when source is absent");
        Fresh("Tools");
        var output = Named<TextBox>("Diagnostics output");
        var diagnostic = RunDiagnostic("doctor", output);
        Navigate("Layout");
        await diagnostic;
        Navigate("Tools"); UpdateLayout();
        Check(ReferenceEquals(output, Named<TextBox>("Diagnostics output")) && output.Text.Length > 20 && !output.Text.Contains("Object reference"), "Diagnostics complete into their original Tools view when the user changes tabs");
        Elements<Expander>(PageBody).Single(item => item.Header?.ToString() == "Advanced JSON editor").IsExpanded = true; UpdateLayout();
        var raw = Named<TextBox>("Complete configuration JSON");
        var validRaw = raw.Text;
        foreach (var invalid in new[] { "{", "[]", "null" })
        {
            raw.Text = invalid;
            var configBefore = File.ReadAllText(backend.ConfigPath);
            var rejected = false;
            try { await SaveDraft(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && File.ReadAllText(backend.ConfigPath) == configBefore && raw.Tag is TextBlock message && message.Visibility == Visibility.Visible && raw.IsKeyboardFocused, "Invalid configuration JSON is revealed, focused and rejected without a write: " + invalid);
        }
        raw.Text = validRaw;
        await SaveDraft();
        Fresh("Layout");
        var originalLabel = S(backend.Config?["display"]?["token_label"]);
        Named<TextBox>("Token label").Text = "Unsaved label";
        await ReloadDraft(false); UpdateLayout();
        Check(!dirty && Named<TextBox>("Token label").Text == originalLabel, "Reload discards the current draft even when the saved configuration is unchanged");
        Named<TextBox>("Token label").Text = "Discard me";
        var reload = ReloadDraft(false);
        Navigate("Privacy"); UpdateLayout();
        Named<TextBox>("Custom text").Text = "Retain my newer privacy draft";
        await reload;
        Check(page == "Privacy" && dirty && Named<TextBox>("Custom text").Text == "Retain my newer privacy draft" && !editors.ContainsKey("Layout"), "Reload discards only its originating tab while preserving another tab's newer edits");
        Fresh("Layout");
        reload = ReloadDraft(false);
        var editingDuringReload = Named<TextBox>("Token label"); editingDuringReload.Text = "Newer edit";
        await reload;
        Check(dirty && ReferenceEquals(editingDuringReload, Named<TextBox>("Token label")) && editingDuringReload.Text == "Newer edit", "Edits made while Reload is pending remain unsaved and are not replaced");
        Fresh("Settings"); ShowSettingsSection("Advanced"); UpdateLayout();
        Elements<Expander>(PageBody).Single(item => item.Header?.ToString() == "Pricing aliases & overrides (JSON)").IsExpanded = true; UpdateLayout();
        var pricing = Named<TextBox>("Pricing JSON"); var validPricing = pricing.Text;
        pricing.Text = "[]";
        var invalidPricing = false;
        var configBeforePricing = File.ReadAllText(backend.ConfigPath);
        try { await SaveDraft(); } catch (InvalidOperationException) { invalidPricing = true; }
        Check(invalidPricing && pricing.Tag is TextBlock pricingError && pricingError.Visibility == Visibility.Visible && pricing.IsKeyboardFocused && File.ReadAllText(backend.ConfigPath) == configBeforePricing, "Invalid pricing JSON exposes an inline error and preserves the saved configuration");
        pricing.Text = validPricing;
        var home = Named<TextBox>("Codex home folder"); var validHome = home.Text;
        home.Text = "relative-folder";
        var invalidHome = false;
        try { await SaveDraft(); } catch (InvalidOperationException) { invalidHome = true; }
        Check(invalidHome && home.Tag is TextBlock homeError && homeError.Visibility == Visibility.Visible && home.IsKeyboardFocused && File.ReadAllText(backend.ConfigPath) == configBeforePricing, "Invalid home folders show a focused inline error before writing settings");
        home.Text = validHome;
        Check(home.Tag is TextBlock cleared && cleared.Visibility == Visibility.Collapsed && AutomationProperties.GetHelpText(home) == "", "Correcting an invalid input clears its error state");
        await ReloadDraft(false);
        Fresh("Layout");
        var grouped = Named<CheckBox>("Group model, tokens and context"); grouped.IsChecked = true; UpdateLayout();
        Check(Elements<ComboBox>(PageBody).Where(combo => AutomationProperties.GetName(combo).EndsWith(" text field")).All(combo => !combo.IsEnabled), "Grouped presence disables field-row choices that do not apply");
        grouped.IsChecked = false;
        Check(Elements<ComboBox>(PageBody).Where(combo => AutomationProperties.GetName(combo).EndsWith(" text field")).All(combo => combo.IsEnabled), "Custom presence layout enables explicit text-row choices");
        var fieldsBefore = draft!["display"]!["presence_layout"]!["fields"]!.AsArray().Select(field => S(field?["field"])).ToArray();
        var moveDown = Elements<Button>(PageBody).First(button => AutomationProperties.GetName(button).StartsWith("Move ") && AutomationProperties.GetName(button).EndsWith(" down") && button.IsEnabled);
        moveDown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var fieldsAfter = draft["display"]!["presence_layout"]!["fields"]!.AsArray().Select(field => S(field?["field"])).ToArray();
        Check(fieldsAfter[0] == fieldsBefore[1] && fieldsAfter[1] == fieldsBefore[0] && fieldsAfter.Length == fieldsBefore.Length && fieldsAfter.Distinct().Count() == fieldsBefore.Distinct().Count(), "Reordering fields swaps their positions without losing or duplicating any field");
        Fresh("Layout");
        Named<TextBox>("Token label").Text = "Keep this draft";
        var configBeforeExit = File.ReadAllText(backend.ConfigPath);
        Hide();
        await ExitApp(); UpdateLayout();
        Check(IsVisible && DecisionPanel.Visibility == Visibility.Visible && !MainContent.IsEnabled && backend.Running, "Exit with a hidden unsaved draft shows a visible decision and keeps monitoring alive");
        Elements<Button>(DecisionBody).Single(button => AutomationProperties.GetName(button) == "Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(DecisionPanel.Visibility == Visibility.Collapsed && MainContent.IsEnabled && dirty && File.ReadAllText(backend.ConfigPath) == configBeforeExit, "Canceling Exit preserves both the draft and saved settings");
        await ExitApp();
        Elements<Button>(DecisionBody).Single(button => AutomationProperties.GetName(button) == "Review changes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(page == "Layout" && dirty && Named<TextBox>("Token label").Text == "Keep this draft", "Review changes restores the tab containing the unsaved edits");
        var beforeCloseBusy = busy; busy = false;
        Close(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); UpdateLayout();
        Check(IsVisible && DecisionPanel.Visibility == Visibility.Visible && backend.Running, "The actual window Close event safely opens the unsaved-changes decision");
        CloseDecision(); busy = beforeCloseBusy;
        Fresh("Overview");
        Check(new[] { "Minimize window", "Maximize window", "Close window" }.All(name => Elements<Button>(this).Any(button => AutomationProperties.GetName(button) == name)) && AutomationProperties.GetLiveSetting(Footer) == AutomationLiveSetting.Polite, "Window controls have accessible names and status changes are announced politely");
        var savedBusy = busy;
        busy = false;
        var gate = new TaskCompletionSource();
        var executions = 0;
        var first = Guard(async () => { executions++; await gate.Task; });
        Navigate("Tools"); UpdateLayout();
        var actionable = Elements<Button>(PageBody).Where(button => button.Tag is string tag && tag == "AsyncAction").ToArray();
        Check(actionable.Length > 0 && actionable.All(button => !button.IsEnabled) && navigation.Values.All(button => button.IsEnabled), "Pending requests disable duplicate actions while page navigation remains available");
        await Guard(() => { executions++; return Task.CompletedTask; });
        gate.SetResult(); await first;
        Check(executions == 1 && actionable.All(button => button.IsEnabled), "Repeated actions run once and controls recover after completion");
        busy = savedBusy;
        UpdateActionAvailability();
        var timeoutStart = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        timeoutStart.ArgumentList.Add("-NoProfile"); timeoutStart.ArgumentList.Add("-Command"); timeoutStart.ArgumentList.Add("Start-Sleep -Seconds 5");
        var timedOut = false;
        var timer = Stopwatch.StartNew();
        try { await Backend.RunCommand(timeoutStart, null, TimeSpan.FromMilliseconds(250)); } catch (IOException error) { timedOut = error.Message.Contains("timed out"); }
        Check(timedOut && timer.Elapsed < TimeSpan.FromSeconds(4) && backend.Running, "A stalled command times out promptly without affecting the monitoring engine");
        var normalWidth = Width; var normalHeight = Height;
        foreach (var width in new[] { 320, 375, 390, 430, 768, 1024, 1280, 1440, 1920 })
        {
            Width = width; Height = 530;
            foreach (var target in navigation.Keys)
            {
                Fresh(target);
                if (target == "Settings") foreach (var section in new[] { "Appearance", "Activity", "Background", "Advanced" }) { ShowSettingsSection(section); UpdateLayout(); VerifyFit(width, target + " / " + section); }
                else VerifyFit(width, target);
            }
            if (width is 320 or 375 or 1920)
            {
                Fresh("Layout");
                var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(evidence, "Audit-Layout-" + width + ".png")); encoder.Save(file);
            }
        }
        Width = 320; Height = 420; Fresh("Settings"); ShowSettingsSection("Advanced"); UpdateLayout();
        Check(PageScroll.ViewportHeight > 70 && SaveHost.ActualHeight >= 36 && StatusPanel.ActualHeight > 30, "The minimum-height window retains scrolling, save controls and status feedback");
        var vertical = (ScrollBar)PageScroll.Template.FindName("PART_VerticalScrollBar", PageScroll);
        var after = Elements<RepeatButton>(vertical).Single(button => button.Command == ScrollBar.PageDownCommand);
        ((System.Windows.Input.RoutedCommand)after.Command).Execute(null, after); UpdateLayout();
        Check(PageScroll.VerticalOffset > 0, "Clicking below the scrollbar thumb advances the page");
        Width = normalWidth; Height = normalHeight; Fresh("Overview");

        void VerifyFit(int width, string target)
        {
            var failures = Elements<FrameworkElement>(PageBody).Where(element => element.IsVisible && element.ActualWidth > 0 && element is Button or ComboBox or TextBox or CheckBox or Border).Where(element =>
            {
                var bounds = element.TransformToAncestor(PageBody).TransformBounds(new Rect(element.RenderSize));
                return bounds.Left < -1 || bounds.Right > PageBody.ActualWidth + 1;
            }).Select(element => element.GetType().Name + ":" + AutomationProperties.GetName(element)).ToArray();
            Check(failures.Length == 0 && PageBody.ActualWidth <= PageScroll.ActualWidth + 1 && navigation.Values.All(button => button.ActualWidth > 30), width + " px " + target + " fits horizontally: " + string.Join(", ", failures));
        }
    }
}
