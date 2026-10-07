using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexPresence;

public partial class MainWindow
{
    private string ConnectionStatus(JsonObject? snapshot)
    {
        if (!backend.Running) return backend.LastError == null ? "Stopped · choose Start presence to connect" : "Engine stopped · see Tools for details";
        if (backend.Starting && snapshot == null) return "Starting · reading Codex sessions…";
        var status = S(snapshot?["discord_status"], "Connecting to Discord…");
        if (status.StartsWith("Connected", StringComparison.OrdinalIgnoreCase) || status is "Monitoring only" or "Hidden · idle timeout reached" or "Priority app active · Codex presence cleared" or "Paused") return status;
        if (status.Contains("error", StringComparison.OrdinalIgnoreCase) || status.Contains("failed", StringComparison.OrdinalIgnoreCase)) return "Waiting for Discord · retrying automatically. Details are in Tools.";
        return status;
    }

    private Expander LiveSession(string id, bool initiallyOpen, JsonObject? initialSnapshot, Func<JsonObject?, JsonNode?> session)
    {
        var updates = new List<Action<JsonObject?>>();
        var latest = initialSnapshot;
        var expander = new Expander { Tag = id, IsExpanded = expanded.TryGetValue(id, out var open) ? open : initiallyOpen };
        void Build()
        {
            if (expander.Content != null) return;
            var detail = new StackPanel();
            LiveRow(detail, "Chat title / project", s => S(session(s)?["project_name"]), updates);
            LiveRow(detail, "Model / effort", s => S(session(s)?["model"]) + " / " + S(session(s)?["reasoning_effort"], "—"), updates);
            LiveRow(detail, "Activity", s => Label(S(session(s)?["activity"]?["kind"], "idle")) + " " + S(session(s)?["activity"]?["target"], ""), updates);
            LiveRow(detail, "Branch", s => S(session(s)?["git_branch"], "—"), updates);
            LiveRow(detail, "Context remaining", s => Percent(session(s)?["context_window"]?["remaining_percent"]), updates);
            LiveRow(detail, "Cost", s => Cost(session(s)?["known_cost_usd"], session(s)?["pricing_status"]), updates);
            detail.Children.Add(LiveJsonViewer("All session data", session, updates, id + ":json"));
            expander.Content = detail;
        }
        void Refresh(JsonObject? snapshot)
        {
            latest = snapshot;
            var header = S(session(snapshot)?["project_name"]) + (S(session(snapshot)?["session_id"]) == S(snapshot?["active_session_id"]) ? "  · selected" : "");
            if (expander.Header?.ToString() != header) { expander.Header = header; AutomationProperties.SetName(expander, header); }
            if (expander.IsExpanded) { Build(); foreach (var update in updates) update(snapshot); }
        }
        expander.Expanded += (_, eventArgs) => { if (!ReferenceEquals(eventArgs.Source, expander)) return; expanded[id] = true; Refresh(latest); };
        expander.Collapsed += (_, eventArgs) => { if (ReferenceEquals(eventArgs.Source, expander)) expanded[id] = false; };
        overviewUpdates.Add(Refresh);
        return expander;
    }

    private async Task QualityChecks(List<string> checks)
    {
        void Check(bool result, string label) { if (!result) throw new InvalidOperationException("Verification failed: " + label); checks.Add(label); }
        void Fresh(string target) { dirty = false; editors.Clear(); draft = null; draftBase = null; Navigate(target); UpdateLayout(); }
        Check(Speed(null) == "Unavailable" && Speed(JsonValue.Create("Unknown")) == "Unavailable" && Speed(JsonValue.Create("On")) == "Fast" && Speed(JsonValue.Create("Off")) == "Standard", "Speed distinguishes unknown, Fast and Standard without inventing telemetry");
        Check(Count(JsonValue.Create("invalid")) == "—" && Percent(null) == "Unavailable" && Cost(JsonValue.Create("invalid"), null) == "Unavailable" && N(JsonValue.Create("Infinity")) == 0, "Missing or malformed numeric telemetry stays unavailable and cannot break layout");
        var rawError = new InvalidOperationException("Developer-only failure details");
        ReportError(rawError);
        Check(!Footer.Text.Contains(rawError.Message) && logs.Last().Contains(rawError.Message), "Action failures show recovery guidance while keeping technical details in Tools");
        Check(UserFeedback.Describe(new UserActionException("Choose a valid duration.")) == "Choose a valid duration." && UserFeedback.Describe(new UnauthorizedAccessException()).Contains("Access was denied") && UserFeedback.Describe(new IOException("Another app changed these settings. Reload before saving.")).Contains("Reload before saving"), "Validation, permissions and conflicting settings provide specific recovery guidance");
        var damagedRoot = Path.Combine(root, "Data", "Verification", "QualityDamagedPreferences");
        Directory.CreateDirectory(Path.Combine(damagedRoot, "Data"));
        File.WriteAllText(Path.Combine(damagedRoot, "Data", "app-settings.json"), "{ invalid preferences");
        Preferences.Load(damagedRoot, out var warning, out var diagnostic);
        Check(warning != null && !warning.Contains("JsonException") && diagnostic != null && diagnostic.Contains("JsonException"), "Damaged preferences keep technical diagnostics separate from recovery guidance");
        NavigateFromKeyboard("Settings"); UpdateLayout();
        Check(navigation["Settings"].IsKeyboardFocused, "Keyboard tab shortcuts place focus on the active tab instead of a removed field");
        settingsSection = "Appearance"; Fresh("Settings"); UpdateLayout();
        Check(!Elements<TextBox>(PageBody).Any(box => AutomationProperties.GetName(box) == "Pricing JSON") && !Elements<ComboBox>(PageBody).Any(combo => AutomationProperties.GetName(combo) == "Application source"), "Unvisited Settings sections do not create hidden editors");
        ShowSettingsSection("Activity"); UpdateLayout();
        var timer = Elements<ComboBox>(PageBody).Single(combo => AutomationProperties.GetName(combo) == "Presence timer");
        timer.SelectedValue = S(timer.SelectedValue == null ? null : JsonValue.Create(timer.SelectedValue.ToString())) == "continuous" ? "work" : "continuous";
        var changedTimer = timer.SelectedValue;
        ShowSettingsSection("Background"); Navigate("Layout"); Navigate("Settings"); ShowSettingsSection("Activity"); UpdateLayout();
        Check(ReferenceEquals(timer, Elements<ComboBox>(PageBody).Single(combo => AutomationProperties.GetName(combo) == "Presence timer")) && Equals(timer.SelectedValue, changedTimer) && dirty, "Lazy Settings sections retain the same controls and unsaved values across tabs");
        Fresh("Overview");
        var sample = QualitySample(100);
        RenderOverview(sample); UpdateLayout();
        var panels = Elements<Expander>(PageBody).Where(item => item.Tag is string key && key.StartsWith("session:profile-")).ToArray();
        Check(panels.Length == 12 && panels.All(panel => panel.Content == null), "A 100-session dashboard initially builds twelve headers without hidden details");
        var initialPanel = panels[0];
        var more = Elements<Button>(PageBody).Single(button => AutomationProperties.GetName(button) == "Show more sessions");
        while (more.Visibility == Visibility.Visible) more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        UpdateLayout();
        panels = Elements<Expander>(PageBody).Where(item => item.Tag is string key && key.StartsWith("session:profile-")).ToArray();
        Check(panels.Length == 100 && panels.All(panel => panel.Content == null) && ReferenceEquals(initialPanel, panels[0]), "Show more exposes every session in batches while retaining existing controls");
        panels[0].IsExpanded = true; UpdateLayout();
        var firstContent = panels[0].Content;
        Check(firstContent is StackPanel && panels.Skip(1).All(panel => panel.Content == null), "Opening one session builds only that session's details");
        var firstData = Elements<Expander>((DependencyObject)firstContent!).Single();
        firstData.IsExpanded = true; UpdateLayout();
        sample["sessions"]![0]!["project_name"] = "Updated chat title";
        var builds = overviewBuilds; RenderOverview(sample); UpdateLayout();
        Check(ReferenceEquals(firstContent, panels[0].Content) && overviewBuilds == builds && Elements<TextBlock>((DependencyObject)firstContent!).Any(text => text.Text == "Updated chat title"), "Live updates refresh expanded details without rebuilding controls");
        panels[0].IsExpanded = false; sample["sessions"]![0]!["project_name"] = "Changed while collapsed"; RenderOverview(sample); UpdateLayout();
        Check(Elements<TextBlock>((DependencyObject)firstContent!).Any(text => text.Text == "Updated chat title") && panels[0].Header.ToString()!.StartsWith("Changed while collapsed"), "Collapsed details stop refreshing while their session headers stay current");
        panels[0].IsExpanded = true; UpdateLayout();
        Check(Elements<TextBlock>((DependencyObject)firstContent!).Any(text => text.Text == "Changed while collapsed"), "Reopening a retained session immediately receives the latest data");
        panels[1].IsExpanded = true; UpdateLayout();
        Check(!Elements<Expander>((DependencyObject)panels[1].Content).Single().IsExpanded && firstData.IsExpanded, "Full-data expansion belongs to one session instead of leaking to others");
        sample["sessions"]![0]!["project_name"] = new string('W', 240); RenderOverview(sample); UpdateLayout();
        Check(PageBody.ActualWidth <= PageScroll.ActualWidth, "Long chat titles wrap within the dashboard without horizontal overflow");
        var values = Elements<Grid>((DependencyObject)firstContent!).Where(grid => grid.Children.Count == 2).Select(grid => grid.Children[1]).OfType<TextBlock>().ToArray();
        Check(values.Length == 6 && values.All(text => Typography.GetNumeralAlignment(text) == FontNumeralAlignment.Tabular), "Live session values use tabular numbers to prevent width jitter");
        var icon = Elements<System.Windows.Shapes.Path>(PageBody).Single(path => path.Width == 22);
        var progress = Elements<ProgressBar>(PageBody).First();
        var originalPreferences = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(backend.Options))!;
        Appearance.Apply(this, new Preferences { InterfaceStyle = "paper", ColorMode = "light", ColorTheme = "forest" }); UpdateLayout();
        Check(ReferenceEquals(icon.Stroke, FindResource("Text")) && ReferenceEquals(icon.Data, FindResource("BrandIcon")) && ReferenceEquals(progress.Foreground, FindResource("Accent")), "Retained dashboard icons and quota bars follow live theme changes");
        double Luminance(string key)
        {
            var color = ((SolidColorBrush)FindResource(key)).Color;
            double Linear(byte component) { var value = component / 255d; return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4); }
            return Linear(color.R) * .2126 + Linear(color.G) * .7152 + Linear(color.B) * .0722;
        }
        foreach (var theme in new[] { "neutral", "slate", "forest", "dusk" })
            foreach (var mode in new[] { "dark", "light" })
            {
                Appearance.Apply(this, new Preferences { ColorTheme = theme, ColorMode = mode });
                var border = Luminance("InputLine");
                Check(new[] { "Canvas", "Control", "Surface" }.All(key => { var background = Luminance(key); return (Math.Max(border, background) + .05) / (Math.Min(border, background) + .05) >= 3; }), theme + " / " + mode + " input boundaries meet 3:1 non-text contrast");
            }
        Appearance.Apply(this, originalPreferences);
        Fresh("Layout");
        var label = Elements<TextBox>(PageBody).Single(box => AutomationProperties.GetName(box) == "Token label");
        label.BringIntoView(); label.Focus();
        ShowExitDecision(); UpdateLayout();
        Check(AutomationProperties.GetName(DecisionPanel) == "Unsaved changes" && System.Windows.Input.KeyboardNavigation.GetTabNavigation(DecisionBody) == System.Windows.Input.KeyboardNavigationMode.Cycle, "The exit decision has an accessible name and contains keyboard focus");
        CloseDecision();
        Check(label.IsKeyboardFocused, "Canceling the exit decision restores focus to the original field");
        Fresh("Overview");
        var originalConfig = File.ReadAllText(backend.ConfigPath);
        var external = JsonNode.Parse(originalConfig)!.AsObject(); external["presence_enabled"] = true;
        File.WriteAllText(backend.ConfigPath, external.ToJsonString(Backend.JsonOptions));
        for (var attempt = 0; attempt < 35 && !B(backend.Snapshot?["publication_enabled"]); attempt++) await Task.Delay(100);
        UpdateOverviewControls();
        Check(backend.PublicationEnabled && !B(backend.Snapshot?["presence_enabled"]) && B(backend.Snapshot?["monitoring_only"]) && overviewPause!.Content.ToString() == "Pause", "External publication changes update Pause while local-only monitoring remains unpublished");
        File.WriteAllText(backend.ConfigPath, originalConfig);
        for (var attempt = 0; attempt < 35 && B(backend.Snapshot?["publication_enabled"]); attempt++) await Task.Delay(100);
        await backend.LoadConfig(); UpdateOverviewControls();
        Check(!backend.PublicationEnabled && overviewPause!.Content.ToString() == "Resume" && backend.Running, "External pause updates Resume without restarting the engine");
        Check(ConnectionStatus(new JsonObject { ["discord_status"] = "Discord error: developer details" }).Contains("retrying automatically"), "Discord connection errors provide reconnect guidance without raw errors");
    }

    private JsonObject QualitySample(int count)
    {
        var sample = backend.Snapshot!.DeepClone().AsObject();
        var template = sample["sessions"]![0]!.DeepClone().AsObject();
        var sessions = new JsonArray();
        for (var index = 0; index < count; index++)
        {
            var session = template.DeepClone().AsObject();
            session["session_id"] = "profile-session-" + index;
            session["project_name"] = "Profile project " + index;
            sessions.Add(session);
        }
        sample["sessions"] = sessions;
        sample["active_session_id"] = "profile-session-0";
        return sample;
    }

    private void ProfileQuality()
    {
        var original = backend.Snapshot!;
        var sample = QualitySample(100);
        var results = new List<object>();
        void Measure(string name, int repetitions, Action action)
        {
            action();
            var times = new List<double>();
            var allocation = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < repetitions; index++)
            {
                var timer = Stopwatch.StartNew(); action(); timer.Stop(); times.Add(timer.Elapsed.TotalMilliseconds);
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocation;
            times.Sort();
            results.Add(new { name, repetitions, medianMs = times[times.Count / 2], p95Ms = times[(int)Math.Floor((times.Count - 1) * 0.95)], allocatedBytesPerOperation = allocated / repetitions });
        }
        dirty = false; editors.Clear(); draft = null; draftBase = null; Navigate("Overview"); UpdateLayout();
        Measure("dashboard_build_100_sessions", 5, () => { overviewSignature = null; RenderOverview(sample); UpdateLayout(); });
        var tokens = 10000;
        Measure("dashboard_refresh_100_sessions", 30, () => { sample["metrics"]!["totals"]!["total_tokens"] = tokens++; RenderOverview(sample); UpdateLayout(); });
        Measure("settings_first_render", 10, () => { dirty = false; editors.Clear(); draft = null; draftBase = null; Navigate("Settings"); UpdateLayout(); });
        dirty = false; editors.Clear(); draft = null; draftBase = null; Navigate("Overview"); RenderOverview(original); UpdateLayout();
        var path = Path.Combine(root, "Data", "Verification", "performance.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3), windowWidth = ActualWidth, syntheticSessions = 100, results }, Backend.JsonOptions));
    }
}
