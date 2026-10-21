using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace CodexPresence;

public partial class MainWindow
{
    private JsonObject? manualProfile;
    private bool manualProfileUnsaved;
    private Action? refreshManualSessions;
    private string ManualProfilePath => testMode ? Path.Combine(backend.Options.CodexHome, "manual-presence.json") : Path.Combine(root, "Data", "manual-presence.json");

    private JsonObject LoadManualProfile()
    {
        if (manualProfile != null) return manualProfile;
        if (File.Exists(ManualProfilePath))
        {
            try { manualProfile = JsonNode.Parse(File.ReadAllText(ManualProfilePath))?.AsObject(); }
            catch (Exception error) when (error is System.Text.Json.JsonException or IOException) { ReportError(new IOException("The saved manual profile could not be read. The original file is preserved.", error)); }
        }
        return manualProfile ??= new JsonObject { ["details"] = "Thinking · My project", ["state"] = "", ["timestamp_mode"] = "session", ["start"] = DateTime.Now.AddHours(-1).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), ["end"] = "" };
    }

    private async Task SaveManualProfile()
    {
        var path = ManualProfilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        var saved = LoadManualProfile().ToJsonString(Backend.JsonOptions);
        await File.WriteAllTextAsync(temporary, saved);
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
        manualProfileUnsaved = LoadManualProfile().ToJsonString(Backend.JsonOptions) != saved;
        Footer.Text = "Manual profile saved.";
    }

    private Task ReloadManualProfile()
    {
        if (manualProfileUnsaved && MessageBox.Show(this, "Discard manual profile changes and reload the saved profile?", "Reload profile", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return Task.CompletedTask;
        manualProfile = null; manualProfileUnsaved = false; draft = null; draftBase = null;
        editors.Remove("Manual");
        Navigate("Manual");
        Footer.Text = "Manual profile reloaded.";
        return Task.CompletedTask;
    }

    private JsonObject ValidManualCommand()
    {
        var profile = LoadManualProfile();
        var details = S(profile["details"], "").Trim(); var state = S(profile["state"], "").Trim();
        var source = S(profile["source"], "custom");
        if (source != "custom" && source != "recent") throw new IOException("Choose a presence source.");
        if (source == "custom" && (details.Length == 0 || details.Length > 128 || state.Length > 128 || details.Concat(state).Any(char.IsControl))) throw new IOException("Enter an activity line and use at most 128 characters per line, without line breaks.");
        if (source == "recent" && backend.Snapshot?["recent_sessions"]?.AsArray().Count is not > 0) throw new IOException("Start the engine and open a Codex chat first, then choose a recent session.");
        if (source == "recent" && S(profile["session_id"], "").Length == 0) profile["session_id"] = S(backend.Snapshot?["recent_sessions"]?.AsArray().FirstOrDefault()?["session_id"], "");
        var mode = S(profile["timestamp_mode"], "session");
        if (!new[] { "connection", "update", "session", "local", "custom" }.Contains(mode)) throw new IOException("Choose a timestamp option.");
        long? ParseTime(string key)
        {
            var text = S(profile[key], "");
            if (text.Length == 0) return null;
            if (!DateTime.TryParseExact(text, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) || TimeZoneInfo.Local.IsInvalidTime(time)) throw new IOException("Enter a valid local date and time, such as 2026-10-21T14:30.");
            return new DateTimeOffset(time).ToUnixTimeSeconds();
        }
        long? start = null, end = null;
        if (mode == "custom")
        {
            start = ParseTime("start"); end = ParseTime("end");
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (start is null or <= 0 || start > now) throw new IOException("Choose a start time in the past or present.");
            if (end != null && (end <= start || end <= now)) throw new IOException("Choose an end time later than the start time and now, or leave End empty.");
        }
        return new JsonObject { ["source"] = source, ["session_id"] = S(profile["session_id"], ""), ["details"] = details, ["state"] = state, ["timestamp_mode"] = mode, ["start_epoch"] = start, ["end_epoch"] = end };
    }

    private async Task ApplyManualPresence()
    {
        var command = ValidManualCommand();
        await SaveManualProfile();
        if (!backend.Running) await StartEngine();
        await backend.SetManualPresence(command);
        Footer.Text = backend.Options.MonitorOnly ? "Manual preview active · local-only monitoring." : "Manual presence selected. Pause and app priority settings still apply.";
    }

    private void RenderManual()
    {
        Heading("Manual presence", "Choose a recent Codex chat or custom text, then set your timeline.");
        var profile = LoadManualProfile();
        var sourceCard = Card("Presence source", "Use the same fields as automatic presence, or write your own text. Choose a chat, then Apply to publish it with your timeline.");
        var sourceChoice = new ComboBox { ItemsSource = new[] { "Custom text", "Recent Codex session" }, SelectedIndex = S(profile["source"], "custom") == "recent" ? 1 : 0, Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(sourceChoice, "Presence source");
        sourceCard.Children.Add(sourceChoice);
        sourceCard.Children.Add(Text("Recent chat or project", 13));
        var sessionChoice = new ComboBox { Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(sessionChoice, "Recent Codex session");
        sourceCard.Children.Add(sessionChoice);
        var sessionHelp = Text("", 13);
        sourceCard.Children.Add(sessionHelp);
        var startEngine = AsyncButton("Load Codex sessions", StartEngine);
        sourceCard.Children.Add(startEngine);
        var sessionIds = Array.Empty<string>();
        var refreshing = false;
        var choicesSignature = "";
        sessionChoice.SelectionChanged += (_, _) =>
        {
            if (!refreshing && sessionChoice.SelectedIndex >= 0 && sessionChoice.SelectedIndex < sessionIds.Length)
            {
                profile["session_id"] = sessionIds[sessionChoice.SelectedIndex];
                manualProfileUnsaved = true;
            }
        };
        var content = Card("Presence text", "This mode uses your chosen Discord application and artwork. Privacy mode and Pause still apply.");
        refreshManualSessions = () =>
        {
            var sessions = backend.Snapshot?["recent_sessions"]?.AsArray();
            var signature = sessions?.ToJsonString() ?? "";
            if (signature != choicesSignature && !sessionChoice.IsDropDownOpen && !sessionChoice.IsKeyboardFocusWithin)
            {
                refreshing = true;
                var chosen = S(profile["session_id"], "");
                sessionIds = sessions?.Select(session => S(session?["session_id"], "")).ToArray() ?? Array.Empty<string>();
                var labels = sessions?.Select((session, index) => (index == 0 ? "Most recent · " : "") + S(session?["project_name"], "Untitled chat") + " · " + S(session?["session_id"], "")[..Math.Min(8, S(session?["session_id"], "").Length)]).ToArray() ?? Array.Empty<string>();
                sessionChoice.ItemsSource = labels;
                sessionChoice.SelectedIndex = chosen.Length == 0 ? (sessionIds.Length > 0 ? 0 : -1) : Array.IndexOf(sessionIds, chosen);
                refreshing = false;
                choicesSignature = signature;
            }
            sessionChoice.IsEnabled = sourceChoice.SelectedIndex == 1 && sessionIds.Length > 0;
            if (content.Parent is FrameworkElement textCard) textCard.Visibility = sourceChoice.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            startEngine.Visibility = backend.Running ? Visibility.Collapsed : Visibility.Visible;
            sessionHelp.Text = sessionIds.Length == 0 ? "Start the engine to load your recent Codex chats. If none appear, open a chat in Codex first." : sessionChoice.SelectedIndex < 0 ? "The saved chat is no longer in the recent list. Choose another chat." : "Uses the selected chat's activity, model, effort, tokens and context. Layout and Privacy settings still apply. This selection stays on that chat when you work elsewhere.";
        };
        sourceChoice.SelectionChanged += (_, _) => { profile["source"] = sourceChoice.SelectedIndex == 1 ? "recent" : "custom"; manualProfileUnsaved = true; refreshManualSessions(); };
        sessionChoice.SelectionChanged += (_, _) => { if (!refreshing) refreshManualSessions(); };
        TextBox Field(Panel panel, string title, string key, int maximum = 128)
        {
            panel.Children.Add(Text(title, 13));
            var input = new TextBox { Text = S(profile[key], ""), MaxLength = maximum, Margin = new Thickness(0, 0, 0, 12) };
            AutomationProperties.SetName(input, title);
            input.TextChanged += (_, _) => { profile[key] = input.Text; manualProfileUnsaved = true; };
            panel.Children.Add(input);
            return input;
        }
        Field(content, "Activity and project", "details");
        Field(content, "Second line (optional)", "state");
        var timing = Card("Timeline", "Custom times use your PC's local time. Leave End empty for elapsed time; set End for a countdown.");
        var modes = new[] { "connection", "update", "session", "local", "custom" };
        var choice = new ComboBox { ItemsSource = new[] { "Since last Discord connection", "Since last manual update", "Since manual presence started", "Your local clock", "Custom timestamps" }, SelectedIndex = Math.Max(0, Array.IndexOf(modes, S(profile["timestamp_mode"], "session"))), Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(choice, "Timestamp option");
        choice.SelectionChanged += (_, _) => { if (choice.SelectedIndex >= 0) { profile["timestamp_mode"] = modes[choice.SelectedIndex]; manualProfileUnsaved = true; } };
        timing.Children.Add(choice);
        var startField = Field(timing, "Start (local date and time)", "start", 16);
        var endField = Field(timing, "End (optional local date and time)", "end", 16);
        void EnableCustomTimes() { startField.IsEnabled = endField.IsEnabled = choice.SelectedIndex == 4; }
        choice.SelectionChanged += (_, _) => EnableCustomTimes();
        EnableCustomTimes();
        var actions = Card("Controls", "Manual presence starts only when you choose Start or Apply. It never resumes itself after restarting the app. Save profile keeps these fields for next time.");
        var apply = AsyncButton("Start or apply manual presence", ApplyManualPresence);
        apply.Style = (Style)FindResource("PrimaryButton");
        actions.Children.Add(apply);
        actions.Children.Add(AsyncButton("Use automatic presence", async () => { if (backend.Running) await backend.SetManualPresence(null); Footer.Text = "Automatic presence selected."; }));
        refreshManualSessions();
    }
}
