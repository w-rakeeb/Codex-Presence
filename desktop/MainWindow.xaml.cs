using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CodexPresence;

public partial class MainWindow : Window
{
    private readonly string root = AppContext.BaseDirectory;
    private readonly Backend backend;
    private readonly Forms.NotifyIcon tray;
    private readonly List<string> logs = new();
    private readonly Dictionary<string, Button> navigation = new();
    private readonly Dictionary<string, bool> expanded = new();
    private readonly string[] args;
    private string page = "Overview";
    private bool closing;
    private bool busy;
    private bool hasDraftChanges;
    private bool dirty
    {
        get => hasDraftChanges;
        set
        {
            hasDraftChanges = value;
            foreach (var item in navigation) item.Value.Content = item.Key + ((item.Key == page && value) || (item.Key != page && editors.TryGetValue(item.Key, out var editor) && editor.Dirty) ? " •" : "");
        }
    }
    private bool testMode;
    private JsonObject? draft;
    private JsonObject? draftBase;
    private readonly Dictionary<string, EditorPage> editors = new();
    private sealed record EditorPage(JsonObject Base, JsonObject Draft, UIElement[] Content, UIElement? Save, Action[] Collect, Func<Task>? Additional, double Offset, bool Dirty);
    private TextBox? diagnostics;
    private readonly List<Action> collectValues = new();
    private Func<Task>? saveAdditional;
    private readonly List<Action<JsonObject?>> overviewUpdates = new();
    private readonly DispatcherTimer refreshTimer;
    private JsonObject? displayedSnapshot;
    private string? overviewSignature;
    private Button? overviewStart;
    private Button? overviewPause;
    private Button? overviewBackground;
    private int overviewBuilds;
    private readonly DispatcherTimer hostTimer;
    private bool hostWasRunning;
    private bool startedByHost;
    private bool checkingHost;
    private Process? terminalProcess;
    private bool resumeAfterTerminal;
    private bool everShown;
    private Brush Muted => (Brush)FindResource("Muted");
    private Brush Accent => (Brush)FindResource("Accent");
    public int ExitCode { get; private set; }

    public MainWindow(string[] arguments)
    {
        args = arguments;
        testMode = args.Contains("--self-test") || args.Contains("--test-home");
        InitializeComponent();
        var options = testMode ? new Preferences() : Preferences.Load(root);
        if (testMode)
        {
            var index = Array.IndexOf(args, "--test-home");
            options.CodexHome = index >= 0 ? args[index + 1] : Path.Combine(root, "Data", "Verification", "CodexHome");
            options.MonitorOnly = true;
            options.CloseToTray = false;
        }
        backend = new Backend(root, options, testMode);
        Appearance.Apply(this, options);
        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        refreshTimer.Tick += (_, _) =>
        {
            refreshTimer.Stop();
            if (IsVisible && page == "Overview" && !ReferenceEquals(displayedSnapshot, backend.Snapshot)) RenderOverview();
        };
        hostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        hostTimer.Tick += (_, _) => { if (!busy && !checkingHost && !dirty && backend.Options.StartWithChatGpt && !testMode) _ = ObserveHost(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) everShown = true; else refreshTimer.Stop(); };
        foreach (var name in new[] { "Overview", "Privacy", "Layout", "Settings", "Tools" })
        {
            var button = ActionButton(name, () => Navigate(name));
            button.FontSize = 12.5;
            button.Padding = new Thickness(4, 8, 4, 8);
            button.Margin = new Thickness(2, 0, 2, 0);
            navigation[name] = button;
            Navigation.Children.Add(button);
        }
        tray = new Forms.NotifyIcon { Text = "Codex Presence", Visible = !options.HideTrayIcon };
        var iconPath = Path.Combine(root, "Runtime", "app.ico");
        tray.Icon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Application;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show dashboard", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("Run in background", null, (_, _) => Dispatcher.Invoke(() => _ = Guard(RunInBackground)));
        menu.Items.Add("Start presence", null, (_, _) => Dispatcher.Invoke(() => _ = Guard(StartEngine)));
        menu.Items.Add("Pause / resume presence", null, (_, _) => Dispatcher.Invoke(() => _ = Guard(TogglePresence)));
        menu.Items.Add("Stop engine", null, (_, _) => Dispatcher.Invoke(() => _ = Guard(StopEngine)));
        menu.Items.Add("Open original terminal", null, (_, _) => Dispatcher.Invoke(() => _ = Guard(OpenTerminal)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(() => _ = Guard(ExitApp)));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        backend.Updated += snapshot => Dispatcher.BeginInvoke(() =>
        {
            if (closing) return;
            if (tray.Visible) { var text = "Codex Presence · " + S(snapshot["discord_status"]); text = text.Substring(0, Math.Min(63, text.Length)); if (tray.Text != text) tray.Text = text; }
            if (IsVisible && page == "Overview") refreshTimer.Start();
        });
        backend.Logged += line => Dispatcher.BeginInvoke(() =>
        {
            logs.Add(line);
            if (logs.Count > 150) logs.RemoveAt(0);
            if (diagnostics != null && page == "Tools") diagnostics.Text = string.Join(Environment.NewLine, logs);
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase)) Footer.Text = line;
        });
        backend.Exited += () => Dispatcher.BeginInvoke(() =>
        {
            if (page == "Overview" && IsVisible) RenderOverview();
            if (backend.LastError != null) Footer.Text = backend.LastError;
        });
        Closing += OnClosing;
        PreviewKeyDown += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (e.Key == Key.S && SaveHost.Visibility == Visibility.Visible) { e.Handled = true; _ = Guard(() => SaveDraft()); }
            else if (e.Key == Key.Tab)
            {
                var names = navigation.Keys.ToArray(); var step = (Keyboard.Modifiers & ModifierKeys.Shift) == 0 ? 1 : -1;
                Navigate(names[(Array.IndexOf(names, page) + step + names.Length) % names.Length]); e.Handled = true;
            }
            else if (e.Key >= Key.D1 && e.Key <= Key.D5) { Navigate(navigation.Keys.ElementAt(e.Key - Key.D1)); e.Handled = true; }
        };
    }

    public Task InitializeApp() => Guard(async () =>
    {
        await backend.LoadConfig();
        Navigate("Overview");
        ApplyDesktopPreferences();
        if (args.Contains("--watch")) { Hide(); await CheckHost(); }
        else if (args.Contains("--startup") || (!testMode && backend.Options.StartInBackground)) await RunInBackground();
        else { ShowDashboard(); if (!testMode && backend.Options.StartEngineOnOpen) await StartEngine(); }
        if (args.Contains("--self-test")) await SelfTest();
        if (args.Contains("--interactive-test"))
        {
            var local = backend.Config!.DeepClone().AsObject(); local["presence_enabled"] = false; await backend.SaveConfig(local);
            await StartEngine(); Title = "Codex Presence — Verification"; Navigate("Settings");
        }
        if (args.Contains("--verify-live") || args.Contains("--verify-background")) await VerifyLive();
    });

    private async Task Guard(Func<Task> action)
    {
        if (busy) { Footer.Text = "Finishing the current action…"; return; }
        busy = true;
        UpdateOverviewControls();
        try { await action(); }
        catch (Exception error)
        {
            Footer.Text = error.Message;
            logs.Add(error.Message);
            if (args.Contains("--self-test"))
            {
                Directory.CreateDirectory(Path.Combine(root, "Data", "Verification"));
                File.WriteAllText(Path.Combine(root, "Data", "Verification", "failure.txt"), error.ToString());
                ExitCode = 1;
                if (terminalProcess is { HasExited: false }) { terminalProcess.Kill(); await terminalProcess.WaitForExitAsync(); }
                await backend.Stop();
                closing = true;
                tray.Dispose();
                Application.Current.Shutdown(1);
            }
        }
        finally { busy = false; UpdateOverviewControls(); ResumeTerminalMonitoring(); }
    }

    private void Navigate(string target)
    {
        if (draft != null && draftBase != null && page != "Overview")
        {
            editors[page] = new EditorPage(draftBase, draft, PageBody.Children.Cast<UIElement>().ToArray(), SaveHost.Child, collectValues.ToArray(), saveAdditional, PageScroll.VerticalOffset, dirty);
        }
        else editors.Remove(page);
        dirty = false;
        page = target;
        dirty = false;
        refreshTimer.Stop();
        overviewSignature = null;
        overviewUpdates.Clear();
        overviewStart = overviewPause = overviewBackground = null;
        PageBody.Children.Clear();
        SaveHost.Child = null;
        SaveHost.Visibility = Visibility.Collapsed;
        collectValues.Clear();
        saveAdditional = null;
        diagnostics = null;
        draft = backend.Config?.DeepClone().AsObject();
        draftBase = backend.Config?.DeepClone().AsObject();
        foreach (var item in navigation)
        {
            item.Value.Background = item.Key == page ? (Brush)FindResource("Selected") : Brushes.Transparent;
            item.Value.Foreground = item.Key == page ? Accent : Muted;
            item.Value.BorderThickness = new Thickness(0);
        }
        if (editors.TryGetValue(page, out var retained) && (retained.Dirty || JsonNode.DeepEquals(retained.Base, backend.Config)))
        {
            draft = retained.Draft; draftBase = retained.Base;
            foreach (var element in retained.Content) PageBody.Children.Add(element);
            collectValues.AddRange(retained.Collect); saveAdditional = retained.Additional;
            SaveHost.Child = retained.Save; SaveHost.Visibility = retained.Save == null ? Visibility.Collapsed : Visibility.Visible;
            dirty = retained.Dirty;
            PageScroll.ScrollToVerticalOffset(retained.Offset);
            if (dirty) Footer.Text = "Unsaved changes on this tab.";
            RecordInteractiveState();
            return;
        }
        switch (page)
        {
            case "Overview": RenderOverview(); break;
            case "Privacy": RenderPrivacy(); break;
            case "Layout": RenderLayout(); break;
            case "Settings": RenderSettings(); break;
            case "Tools": RenderTools(); break;
        }
        PageScroll.ScrollToTop();
        RecordInteractiveState();
    }

    private void RecordInteractiveState()
    {
        if (!args.Contains("--interactive-test")) return;
        var evidence = Path.Combine(root, "Data", "Verification"); Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "interactive-results.json"), JsonSerializer.Serialize(new { page, dirty, options = backend.Options, selected = Elements<ComboBox>(PageBody).Select(combo => new { name = AutomationProperties.GetName(combo), value = combo.SelectedValue }), pendingTabs = editors.Where(pair => pair.Value.Dirty).Select(pair => pair.Key), config = backend.Config }, Backend.JsonOptions));
    }

    private TextBlock Text(string value, double size = 14, Brush? color = null)
    {
        var block = new TextBlock { Text = value, FontSize = Math.Max(13, size), TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, color == null ? "Text" : ReferenceEquals(color, Accent) ? "Accent" : "Muted");
        return block;
    }
    private static string S(JsonNode? node, string fallback = "Unavailable") => node == null ? fallback : node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node.ToJsonString();
    private static double N(JsonNode? node) => node is JsonValue value && (value.TryGetValue<double>(out var number) || double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) ? number : 0;
    private static bool B(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var result) && result;
    private static string Label(string text) => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.Replace('_', ' ')).Replace("Usd", "USD").Replace("Wsl", "WSL").Replace("Ai", "AI");
    private static string Count(JsonNode? node) => node == null ? "—" : N(node).ToString("N0");
    private static string Cost(JsonNode? amount, JsonNode? status) => amount == null ? "Unavailable" : "$" + N(amount).ToString("F2", CultureInfo.InvariantCulture) + (S(status) == "partial" ? " · partial" : "");

    private Button ActionButton(string title, Action action)
    {
        var button = new Button { Content = title };
        AutomationProperties.SetName(button, title);
        button.Click += (_, _) => action();
        return button;
    }

    private Button AsyncButton(string title, Func<Task> action) => ActionButton(title, () => _ = Guard(action));

    private StackPanel Card(string title, string? subtitle = null)
    {
        var inner = new StackPanel();
        var heading = Text(Label(title.ToLowerInvariant()), backend.Options.InterfaceStyle == "soft" ? 17 : 14);
        heading.SetResourceReference(TextBlock.FontFamilyProperty, "HeadingFont");
        heading.SetResourceReference(TextBlock.FontWeightProperty, "HeadingWeight");
        heading.SetResourceReference(TextBlock.FontSizeProperty, "CardTitleSize");
        heading.Margin = new Thickness(0, 0, 0, 9);
        inner.Children.Add(heading);
        if (subtitle != null) { var description = Text(subtitle, 11, Muted); description.Margin = new Thickness(0, 0, 0, 12); inner.Children.Add(description); }
        var border = new Border { Child = inner };
        foreach (var resource in new[] { (Border.BackgroundProperty, "Surface"), (Border.BorderBrushProperty, "Line"), (Border.BorderThicknessProperty, "CardBorder"), (Border.CornerRadiusProperty, "CardRadius"), (Border.PaddingProperty, "CardPadding"), (Border.MarginProperty, "CardSpacing") }) border.SetResourceReference(resource.Item1, resource.Item2);
        PageBody.Children.Add(border);
        return inner;
    }

    private void Heading(string title, string description)
    {
        var header = Text(title); header.SetResourceReference(TextBlock.FontSizeProperty, "HeadingSize"); header.SetResourceReference(TextBlock.FontFamilyProperty, "HeadingFont"); header.SetResourceReference(TextBlock.FontWeightProperty, "HeadingWeight");
        PageBody.Children.Add(header);
        var detail = Text(description, 12, Muted); detail.Margin = new Thickness(0, 6, 0, 18);
        PageBody.Children.Add(detail);
    }

    private void Row(Panel panel, string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.40, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.60, GridUnitType.Star) });
        var key = Text(label, 11, Muted); key.Margin = new Thickness(0, 0, 10, 0); grid.Children.Add(key);
        var content = Text(value, 12); Grid.SetColumn(content, 1); grid.Children.Add(content);
        panel.Children.Add(grid);
    }

    private void RenderOverview(JsonObject? value = null)
    {
        var snapshot = value ?? backend.Snapshot;
        var sessions = snapshot?["sessions"]?.AsArray();
        var windows = snapshot?["limits"]?["windows"]?.AsArray();
        var signature = string.Join("|", sessions?.Select(session => S(session?["session_id"])) ?? Array.Empty<string>()) + ";" + string.Join("|", windows?.Select(window => S(window?["window_minutes"])) ?? Array.Empty<string>());
        if (overviewSignature != signature)
        {
            var offset = PageScroll.VerticalOffset;
            overviewSignature = signature;
            overviewBuilds++;
            overviewUpdates.Clear();
            PageBody.Children.Clear();
            Heading("Overview", "Discord connection and current session.");
            var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
            overviewStart = AsyncButton("Start presence", () => backend.Running ? StopEngine() : StartEngine());
            overviewStart.Style = (Style)FindResource("PrimaryButton");
            overviewPause = AsyncButton("Pause", TogglePresence);
            overviewBackground = AsyncButton("Run in background", RunInBackground);
            foreach (var button in new[] { overviewStart, overviewPause, overviewBackground }) { button.Margin = new Thickness(0, 0, 8, 6); controls.Children.Add(button); }
            var exit = AsyncButton("Exit", ExitApp); exit.Margin = new Thickness(0, 0, 0, 6); controls.Children.Add(exit);
            PageBody.Children.Add(controls);
            var preview = Card("DISCORD PRESENCE");
            preview.Children.Add(LiveText(s => backend.Running ? S(s?["discord_status"], "Starting…") : backend.LastError ?? "Stopped · click Start presence to connect", 11, Muted));
            var horizontal = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            horizontal.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) }); horizontal.ColumnDefinitions.Add(new ColumnDefinition());
            horizontal.Children.Add(new Border { Width = 40, Height = 40, Background = (Brush)FindResource("Control"), BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(1), CornerRadius = (CornerRadius)FindResource("InputRadius"), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Child = new System.Windows.Shapes.Path { Data = (Geometry)FindResource("BrandIcon"), Stroke = Foreground, StrokeThickness = 1.6, Width = 22, Height = 22, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            var lines = new StackPanel(); Grid.SetColumn(lines, 1);
            var title = LiveText(s => S(s?["preview"]?["app_name"], "Codex App"), 15); title.FontWeight = FontWeights.SemiBold; lines.Children.Add(title);
            lines.Children.Add(LiveText(s => S(s?["preview"]?["details"], "Waiting for a session"), 12));
            lines.Children.Add(LiveText(s => S(s?["preview"]?["state"], "Ready to connect"), 11, Muted)); horizontal.Children.Add(lines); preview.Children.Add(horizontal);
            var usage = Card("SESSION USAGE");
            LiveRow(usage, "Tokens", s => Count(s?["metrics"]?["totals"]?["total_tokens"]));
            LiveRow(usage, "Known cost", s => Cost(s?["metrics"]?["totals"]?["known_cost_usd"], s?["metrics"]?["totals"]?["pricing_status"]));
            LiveRow(usage, "Input / cached / output", s => Count(s?["metrics"]?["totals"]?["input_tokens"]) + " / " + Count(s?["metrics"]?["totals"]?["cached_input_tokens"]) + " / " + Count(s?["metrics"]?["totals"]?["output_tokens"]));
            LiveRow(usage, "Cache hit", s => s?["metrics"] == null ? "—" : (N(s["metrics"]?["totals"]?["cache_hit_ratio"]) * 100).ToString("F1") + "%");
            LiveRow(usage, "Plan", s => S(s?["plan"]));
            LiveRow(usage, "Speed", s => s?["speed"] == null ? "Unavailable" : S(s["speed"]) == "On" ? "Fast" : "Standard");
            usage.Children.Add(LiveJsonViewer("Plan and speed details", s => new JsonObject { ["plan"] = s?["plan_details"]?.DeepClone(), ["speed"] = s?["service_tier"]?.DeepClone() }));
            var sessionCard = Card("ACTIVE SESSIONS");
            if (sessions == null || sessions.Count == 0) sessionCard.Children.Add(Text("No recent session detected.", 12, Muted));
            else for (var i = 0; i < sessions.Count; i++)
            {
                var index = i;
                JsonNode? Session(JsonObject? s) => s?["sessions"]?[index];
                var detail = new StackPanel();
                LiveRow(detail, "Chat title / project", s => S(Session(s)?["project_name"]));
                LiveRow(detail, "Model / effort", s => S(Session(s)?["model"]) + " / " + S(Session(s)?["reasoning_effort"], "—"));
                LiveRow(detail, "Activity", s => Label(S(Session(s)?["activity"]?["kind"], "idle")) + " " + S(Session(s)?["activity"]?["target"], ""));
                LiveRow(detail, "Branch", s => S(Session(s)?["git_branch"], "—"));
                LiveRow(detail, "Context remaining", s => Session(s)?["context_window"] == null ? "Unavailable" : N(Session(s)?["context_window"]?["remaining_percent"]).ToString("F1") + "%");
                LiveRow(detail, "Cost", s => Cost(Session(s)?["known_cost_usd"], Session(s)?["pricing_status"]));
                detail.Children.Add(LiveJsonViewer("All session data", Session));
                var id = "session:" + S(sessions[i]?["session_id"]);
                var expander = new Expander { Content = detail, IsExpanded = expanded.TryGetValue(id, out var open) ? open : sessions.Count == 1 };
                overviewUpdates.Add(s => expander.Header = S(Session(s)?["project_name"]) + (S(Session(s)?["session_id"]) == S(s?["active_session_id"]) ? "  · selected" : ""));
                expander.Expanded += (_, _) => expanded[id] = true; expander.Collapsed += (_, _) => expanded[id] = false;
                sessionCard.Children.Add(expander);
            }
            var limits = Card("LIMITS & CREDITS");
            LiveRow(limits, "Source", s => S(s?["limits_source"]?["label"]));
            if (windows == null || windows.Count == 0) limits.Children.Add(Text("No quota windows reported.", 11, Muted));
            else for (var i = 0; i < windows.Count; i++)
            {
                var index = i;
                JsonNode? Window(JsonObject? s) => s?["limits"]?["windows"]?[index];
                var minutes = N(windows[i]?["window_minutes"]);
                var name = minutes >= 1440 && minutes % 1440 == 0 ? (minutes / 1440) + " day" : minutes >= 60 && minutes % 60 == 0 ? (minutes / 60) + " hour" : minutes + " minute";
                LiveRow(limits, name + " quota", s => N(Window(s)?["used_percent"]).ToString("F1") + "% used · " + N(Window(s)?["remaining_percent"]).ToString("F1") + "% remaining");
                var progress = new ProgressBar { Maximum = 100, Height = 3, Foreground = Accent, Background = (Brush)FindResource("Line"), BorderThickness = new Thickness(0), Margin = new Thickness(0, 4, 0, 6) };
                overviewUpdates.Add(s => { var next = Math.Clamp(N(Window(s)?["used_percent"]), 0, 100); if (progress.Value != next) progress.Value = next; });
                limits.Children.Add(progress);
                LiveRow(limits, "Resets", s => ResetTime(Window(s)?["resets_at"]));
            }
            LiveRow(limits, "Credit balance", s => s?["credits"] == null ? "Unavailable" : B(s["credits"]?["unlimited"]) ? "Unlimited" : S(s["credits"]?["balance"]));
            limits.Children.Add(LiveJsonViewer("Full quota and credit data", s => new JsonObject { ["limits"] = s?["limits"]?.DeepClone(), ["credits"] = s?["credits"]?.DeepClone(), ["source"] = s?["limits_source"]?.DeepClone() }));
            Card("METRICS").Children.Add(LiveJsonViewer("Cost breakdown and model totals", s => s?["metrics"]));
            PageScroll.ScrollToVerticalOffset(offset);
        }
        foreach (var update in overviewUpdates) update(snapshot);
        displayedSnapshot = snapshot;
        UpdateOverviewControls();
    }

    private TextBlock LiveText(Func<JsonObject?, string> getter, double size = 12, Brush? color = null)
    {
        var text = Text("", size, color);
        overviewUpdates.Add(snapshot => { var next = getter(snapshot); if (text.Text != next) text.Text = next; });
        return text;
    }

    private void LiveRow(Panel panel, string label, Func<JsonObject?, string> getter)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.40, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.60, GridUnitType.Star) });
        var key = Text(label, 11, Muted); key.Margin = new Thickness(0, 0, 10, 0); grid.Children.Add(key);
        var value = LiveText(getter); Grid.SetColumn(value, 1); grid.Children.Add(value); panel.Children.Add(grid);
    }

    private Expander LiveJsonViewer(string title, Func<JsonObject?, JsonNode?> getter)
    {
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) };
        var panel = new Expander { Header = title, Content = text, IsExpanded = expanded.TryGetValue(title, out var open) && open };
        void Update(JsonObject? snapshot) { if (panel.IsExpanded) { var next = getter(snapshot)?.ToJsonString(Backend.JsonOptions) ?? "Unavailable"; if (text.Text != next) text.Text = next; } }
        panel.Expanded += (_, _) => { expanded[title] = true; Update(displayedSnapshot ?? backend.Snapshot); };
        panel.Collapsed += (_, _) => expanded[title] = false;
        overviewUpdates.Add(Update);
        return panel;
    }

    private void UpdateOverviewControls()
    {
        if (overviewStart != null) { overviewStart.Content = backend.Running ? "Stop engine" : "Start presence"; AutomationProperties.SetName(overviewStart, overviewStart.Content.ToString()); overviewStart.IsEnabled = !busy && terminalProcess is not { HasExited: false }; }
        if (overviewPause != null) { overviewPause.Content = B(backend.Config?["presence_enabled"]) ? "Pause" : "Resume"; AutomationProperties.SetName(overviewPause, overviewPause.Content.ToString()); overviewPause.IsEnabled = !busy; }
        if (overviewBackground != null) overviewBackground.IsEnabled = !busy && terminalProcess is not { HasExited: false };
    }

    private string ResetTime(JsonNode? value)
    {
        if (value == null) return "Unavailable";
        if (value is JsonValue number && number.TryGetValue<long>(out var unix)) return DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("MMM d, HH:mm");
        return DateTimeOffset.TryParse(S(value), out var date) ? date.ToLocalTime().ToString("MMM d, HH:mm") : S(value);
    }

    private Expander JsonViewer(string title, JsonNode? value)
    {
        var panel = new Expander { Header = title, IsExpanded = expanded.TryGetValue(title, out var open) && open, Content = new TextBox { Text = value?.ToJsonString(Backend.JsonOptions) ?? "Unavailable", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) } };
        panel.Expanded += (_, _) => expanded[title] = true;
        panel.Collapsed += (_, _) => expanded[title] = false;
        return panel;
    }

    private void RenderPrivacy()
    {
        Heading("Presence visibility", "Choose what appears in Discord. These controls do not hide local dashboard data.");
        if (draft == null) return;
        var master = Card("PUBLICATION");
        EditBoolean(master, draft, "presence_enabled", "Publish Discord presence");
        var privacy = draft["privacy"]!.AsObject();
        EditBoolean(master, privacy, "enabled", "Privacy mode · hide session details");
        var fields = Card("VISIBLE FIELDS", "Project names, branches and activity targets can reveal what you are working on.");
        foreach (var entry in privacy.Where(x => x.Key != "enabled").ToArray()) EditBoolean(fields, privacy, entry.Key, entry.Key == "show_project_name" ? "Chat title / project" : Label(entry.Key.Replace("show_", "")));
        EditText(fields, draft["display"]!.AsObject(), "custom_text", "Custom text", maximum: 128);
        SaveBar();
    }

    private void EditBoolean(Panel panel, JsonObject parent, string key, string label)
    {
        var checkbox = new CheckBox { Content = Text(label, 12), IsChecked = B(parent[key]) };
        AutomationProperties.SetName(checkbox, label);
        checkbox.Checked += (_, _) => { parent[key] = true; dirty = true; };
        checkbox.Unchecked += (_, _) => { parent[key] = false; dirty = true; };
        panel.Children.Add(checkbox);
    }

    private void EditChoice(Panel panel, JsonObject parent, string key, string label, string[] values)
    {
        var holder = new StackPanel { Margin = new Thickness(0, 7, 0, 7) };
        holder.Children.Add(Text(label, 11, Muted));
        var combo = new ComboBox { ItemsSource = values.Select(value => new Choice(value, ChoiceLabel(value))).ToArray(), DisplayMemberPath = "Title", SelectedValuePath = "Key", SelectedValue = S(parent[key], values[0]), Margin = new Thickness(0, 6, 0, 0) };
        AutomationProperties.SetName(combo, label);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedValue is string value) { parent[key] = value; dirty = true; } };
        holder.Children.Add(combo); panel.Children.Add(holder);
    }

    private sealed record Choice(string Key, string Title)
    {
        public override string ToString() => Title;
    }
    private static string ChoiceLabel(string value) => value switch
    {
        "codex_app" => "Codex App", "chat_gpt_app" => "ChatGPT App", "pro_5x" => "Pro 5x", "pro_20x" => "Pro 20x", "auto" => "Automatic", "ascii" => "ASCII", "minimal" => "Minimal", "soft" => "Soft", "chatgpt" => "Rounded", "work" => "Work / project", "continuous" => "Continuous since app opens", _ => Label(value)
    };

    private void EditText(Panel panel, JsonObject parent, string key, string label, bool optional = false, int maximum = 0)
    {
        var holder = new StackPanel { Margin = new Thickness(0, 7, 0, 7) }; holder.Children.Add(Text(label, 11, Muted));
        var box = new TextBox { Text = S(parent[key], ""), ToolTip = S(parent[key], ""), MaxLength = maximum, Margin = new Thickness(0, 6, 0, 0) };
        AutomationProperties.SetName(box, label);
        box.TextChanged += (_, _) => { parent[key] = optional && string.IsNullOrWhiteSpace(box.Text) ? null : JsonValue.Create(box.Text); dirty = true; };
        holder.Children.Add(box); panel.Children.Add(holder);
    }

    private void RenderLayout()
    {
        Heading("Presence layout", "Choose the application, labels and field order for Discord's two text lines.");
        if (draft == null) return;
        var display = draft["display"]!.AsObject();
        EditChoice(Card("DESKTOP IDENTITY"), display, "desktop_presence_design", "Discord application", new[] { "codex_app", "chat_gpt_app" });
        var layout = display["presence_layout"]!.AsObject();
        EditChoice(Card("LABEL STYLE"), layout, "label_style", "Field labels", new[] { "compact", "descriptive" });
        EditBoolean(Card("USAGE LINE"), display, "separate_usage_line", "Tokens and context on a separate line");
        var labels = Card("CUSTOM LABELS", "Chat titles use the latest local rename. Folder names are used when a title is unavailable.");
        EditBoolean(labels, display, "use_chat_title", "Use the chat title for the project field");
        EditText(labels, display, "token_label", "Token label", maximum: 16);
        EditText(labels, display, "context_label", "Context label", maximum: 16);
        EditText(labels, display, "custom_text", "Custom text", maximum: 128);
        var fieldsCard = Card("FIELD ORDER", "Details is the first line; state is the second. Long content is fitted by the original compositor. Privacy controls also apply.");
        var fields = layout["fields"]!.AsArray();
        var list = new StackPanel(); fieldsCard.Children.Add(list);
        void RenderFields()
        {
            list.Children.Clear();
            for (var i = 0; i < fields.Count; i++)
            {
                var index = i;
                var field = fields[i]!.AsObject();
                var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
                foreach (var width in new[] { new GridLength(1, GridUnitType.Star), new GridLength(104), new GridLength(35), new GridLength(35) }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
                var enabled = new CheckBox { Content = S(field["field"]) switch { "project" => "Chat title", "custom" => "Custom text", _ => Label(S(field["field"])) }, IsChecked = B(field["enabled"]) };
                void SetVisibility(bool visible) { field["enabled"] = visible; draft["privacy"]![PrivacyKey(S(field["field"]))] = visible; dirty = true; }
                enabled.Checked += (_, _) => SetVisibility(true);
                enabled.Unchecked += (_, _) => SetVisibility(false); row.Children.Add(enabled);
                var zone = new ComboBox { ItemsSource = new[] { "details", "state" }, SelectedItem = S(field["zone"]), Margin = new Thickness(0, 0, 6, 0), FontSize = 13 };
                zone.SelectionChanged += (_, _) => { field["zone"] = zone.SelectedItem?.ToString(); dirty = true; }; Grid.SetColumn(zone, 1); row.Children.Add(zone);
                var up = ActionButton("↑", () => { var item = fields[index]; fields.RemoveAt(index); fields.Insert(index - 1, item); dirty = true; RenderFields(); }); up.Padding = new Thickness(0); up.IsEnabled = i > 0; up.Margin = new Thickness(0, 0, 4, 0); Grid.SetColumn(up, 2); row.Children.Add(up);
                var down = ActionButton("↓", () => { var item = fields[index]; fields.RemoveAt(index); fields.Insert(index + 1, item); dirty = true; RenderFields(); }); down.Padding = new Thickness(0); down.IsEnabled = i < fields.Count - 1; Grid.SetColumn(down, 3); row.Children.Add(down);
                list.Children.Add(row);
            }
        }
        RenderFields();
        SaveBar();
    }

    private static string PrivacyKey(string field) => field switch
    {
        "project" => "show_project_name", "branch" => "show_git_branch", "quotas" => "show_limits", "custom" => "show_custom_text", _ => "show_" + field
    };

    private void SaveBar(Func<Task>? additional = null)
    {
        saveAdditional = additional;
        var row = new WrapPanel();
        var save = AsyncButton("Save changes", () => SaveDraft(additional));
        save.Style = (Style)FindResource("PrimaryButton"); save.Margin = new Thickness(0, 0, 8, 0); row.Children.Add(save);
        var reload = AsyncButton("Reload", async () => { await backend.LoadConfig(); dirty = false; editors.Remove(page); Navigate(page); Footer.Text = "Settings reloaded."; }); row.Children.Add(reload);
        SaveHost.Child = row;
        SaveHost.Visibility = Visibility.Visible;
    }

    private async Task SaveDraft(Func<Task>? additional = null)
    {
        additional ??= saveAdditional;
        foreach (var collect in collectValues) collect();
        if (draft != null && draftBase != null && backend.Config != null) await backend.SaveConfig(ConfigurationPatch.Merge(draftBase, draft, backend.Config));
        if (additional != null) await additional();
        dirty = false;
        editors.Remove(page);
        Navigate(page);
        Footer.Text = "Changes saved. Presence updates automatically.";
    }

    private void RenderSettings()
    {
        Heading("Settings", "Appearance, presence and startup.");
        if (draft == null) return;
        var preferenceCopy = JsonSerializer.SerializeToNode(backend.Options)!.AsObject();
        var appearance = Card("APPEARANCE");
        EditChoice(appearance, preferenceCopy, "InterfaceStyle", "Interface style", new[] { "minimal", "soft", "chatgpt", "paper", "studio" });
        EditChoice(appearance, preferenceCopy, "ColorMode", "Color mode", new[] { "dark", "light" });
        EditChoice(Card("ELAPSED TIME"), draft["display"]!.AsObject(), "timer_mode", "Presence timer", new[] { "continuous", "work" });
        var planCard = Card("PLAN");
        var plan = draft["openai_plan"]!.AsObject();
        EditChoice(planCard, plan, "mode", "Detection", new[] { "auto", "manual" });
        EditChoice(planCard, plan, "tier", "Manual plan", new[] { "free", "go", "plus", "pro_5x", "pro_20x", "business", "enterprise", "edu" });
        EditBoolean(planCard, plan, "show_price", "Show plan price");
        EditBoolean(planCard, draft["privacy"]!.AsObject(), "show_subscription", "Show subscription in Discord");
        var application = Card("DESKTOP & MONITORING", "Saving monitoring preferences restarts the engine. Startup can run with or without a tray icon.");
        EditText(application, preferenceCopy, "CodexHome", "Codex home folder");
        foreach (var pair in new[] { ("PollSeconds", "Poll interval (seconds)", 1, 60), ("StaleSeconds", "Stale session cutoff (seconds)", 1, 86400), ("StickySeconds", "Keep active session (seconds)", 60, 86400) })
        {
            var box = new TextBox { Text = S(preferenceCopy[pair.Item1]), Margin = new Thickness(0, 6, 0, 12) };
            AutomationProperties.SetName(box, pair.Item2);
            application.Children.Add(Text(pair.Item2, 11, Muted)); application.Children.Add(box);
            box.TextChanged += (_, _) => dirty = true;
            collectValues.Add(() => { if (!int.TryParse(box.Text, out var number) || number < pair.Item3 || number > pair.Item4) throw new InvalidOperationException(pair.Item2 + " must be between " + pair.Item3 + " and " + pair.Item4 + "."); preferenceCopy[pair.Item1] = number; });
        }
        foreach (var pair in new[] { ("CloseToTray", "Keep running when the window closes"), ("HideTrayIcon", "Hide the system tray icon"), ("StartEngineOnOpen", "Start presence when opening this app"), ("StartInBackground", "Open in background and start presence"), ("StartWithChatGpt", "Start with Codex / ChatGPT in background"), ("StartWithWindows", "Start with Windows"), ("MonitorOnly", "Monitor locally without publishing to Discord"), ("IncludeWsl", "Include WSL sessions"), ("EfficiencyMode", "Windows Efficiency mode for the engine") }) EditBoolean(application, preferenceCopy, pair.Item1, pair.Item2);
        application.Children.Add(Text("Automatic launches stay hidden. Use your desktop shortcut or reopen this app to show the dashboard.", 13, Muted));
        var display = draft["display"]!.AsObject();
        var branding = new StackPanel();
        foreach (var key in new[] { "large_image_key", "large_text", "desktop_large_image_key", "desktop_large_text", "small_image_key", "small_text" }) EditText(branding, display, key, Label(key));
        var activityKeys = display["activity_small_image_keys"]!.AsObject();
        foreach (var key in activityKeys.Select(x => x.Key).ToArray()) EditText(branding, activityKeys, key, Label(key) + " icon key (optional)", true);
        EditChoice(branding, display, "terminal_logo_mode", "Original terminal logo mode", new[] { "auto", "ascii", "image" });
        EditText(branding, display, "terminal_logo_path", "Original terminal logo file (optional)", true);
        Card("BRANDING").Children.Add(new Expander { Header = "Images, tooltips & terminal logo", Content = branding });
        var pricing = Card("PRICING", "Override rates per million tokens and model aliases. Save validates the complete configuration before replacing it.");
        var pricingEditor = new TextBox { Text = draft["pricing"]!.ToJsonString(Backend.JsonOptions), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13, MinHeight = 100, MaxHeight = 250, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        pricingEditor.TextChanged += (_, _) => dirty = true;
        pricing.Children.Add(new Expander { Header = "Pricing aliases & overrides (JSON)", Content = pricingEditor });
        collectValues.Add(() => draft["pricing"] = JsonNode.Parse(pricingEditor.Text)?.AsObject() ?? throw new InvalidOperationException("Pricing must be a JSON object."));
        var identity = Card("ENGINE IDENTITY", "Application IDs and the public verification key are maintained by the original engine.");
        Row(identity, "Schema", S(draft["schema_version"]));
        Row(identity, "Default application", S(draft["discord_client_id"]));
        Row(identity, "Desktop application", S(draft["discord_client_id_desktop"]));
        identity.Children.Add(JsonViewer("Public verification key", draft["discord_public_key"]));
        Preferences? pending = null;
        collectValues.Add(() =>
        {
            pending = preferenceCopy.Deserialize<Preferences>()!;
            if (string.IsNullOrWhiteSpace(pending.CodexHome) || !Path.IsPathFullyQualified(pending.CodexHome)) throw new InvalidOperationException("Choose an absolute Codex home folder path.");
            if (!string.Equals(Path.GetFullPath(pending.CodexHome), Path.GetFullPath(backend.Options.CodexHome), StringComparison.OrdinalIgnoreCase) && dirty)
                Footer.Text = "Switching Codex home; reload its presence settings after saving.";
        });
        SaveBar(async () =>
        {
            var restart = backend.Running && !backend.Options.SameMonitoring(pending!);
            if (restart) await backend.Stop();
            backend.Options = pending!;
            Appearance.Apply(this, backend.Options);
            ApplyDesktopPreferences();
            if (!testMode) pending!.ApplyStartup();
            pending!.Save(testMode ? Path.Combine(root, "Data", "Verification", "Preferences") : root);
            await backend.LoadConfig();
            if (restart) await backend.Start();
        });
    }

    private void RenderTools()
    {
        Heading("Tools", "Diagnostics, terminal and files.");
        var tools = Card("DIAGNOSTICS");
        var actions = new WrapPanel();
        foreach (var command in new[] { "doctor", "status" })
        {
            var button = AsyncButton(Label(command), async () =>
            {
                Footer.Text = "Running " + command + "…";
                try { diagnostics!.Text = await backend.Command(command); Footer.Text = "Diagnostics finished."; }
                catch (Exception error) { diagnostics!.Text = error.Message; Footer.Text = "Diagnostics found an issue."; }
            }); button.Margin = new Thickness(0, 0, 8, 8); actions.Children.Add(button);
        }
        tools.Children.Add(actions);
        var terminal = AsyncButton("Open original terminal", OpenTerminal); terminal.Margin = new Thickness(0, 0, 0, 10); tools.Children.Add(terminal);
        var shortcut = ActionButton("Create desktop shortcut", () => { var path = DesktopShortcut.Create(root); Footer.Text = "Desktop shortcut created."; logs.Add(path); }); shortcut.Margin = new Thickness(0, 0, 0, 10); tools.Children.Add(shortcut);
        diagnostics = new TextBox { Text = string.Join(Environment.NewLine, logs), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13, Height = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        tools.Children.Add(diagnostics);
        var advanced = Card("COMPLETE CONFIGURATION", "All engine fields are available here. External edits are detected before saving. Previous files are backed up under Data / Backups.");
        var raw = new TextBox { Text = draft?.ToJsonString(Backend.JsonOptions) ?? "{}", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13, Height = 230, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        raw.TextChanged += (_, _) => dirty = true; advanced.Children.Add(new Expander { Header = "Advanced JSON editor", Content = raw });
        collectValues.Add(() => draft = JsonNode.Parse(raw.Text)?.AsObject() ?? throw new InvalidOperationException("Configuration must be a JSON object."));
        SaveBar();
        var docs = Card("REFERENCE");
        foreach (var pair in new[] { ("A to Z guide", Path.Combine(root, "Guide.md")), ("Original documentation", Path.Combine(root, "Source", "docs", "index.md")), ("Open project folder", root), ("Open config folder", backend.Options.CodexHome) })
        {
            var button = ActionButton(pair.Item1, () => OpenPath(pair.Item2)); button.Margin = new Thickness(0, 4, 0, 4); docs.Children.Add(button);
        }
        Row(docs, "Desktop app", "1.4.0 · Windows x64"); Row(docs, "Presence engine", "Based on xt0n1-t3ch / Codex Discord Rich Presence 1.11.2");
        docs.Children.Add(Text("MIT licensed · original engine attribution and license included.", 11, Muted));
    }

    private static void OpenPath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("This path is not available yet: " + path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private async Task StartEngine()
    {
        Footer.Text = "Starting presence…";
        await backend.LoadConfig();
        await backend.SaveConfig(backend.Config!.DeepClone().AsObject());
        await backend.Start();
        Footer.Text = backend.Options.MonitorOnly ? "Monitoring locally · Discord publication is off." : S(backend.Snapshot?["discord_status"], "Connecting to Discord…");
        if (page == "Overview") RenderOverview();
    }

    private async Task RunInBackground()
    {
        if (!backend.Running) await StartEngine();
        Footer.Text = "Presence is running in the background.";
        Hide();
    }

    private async Task StopEngine() { startedByHost = false; await backend.Stop(); Footer.Text = "Engine stopped. Discord presence cleared."; if (page == "Overview") RenderOverview(); }

    private async Task TogglePresence()
    {
        if (dirty) throw new InvalidOperationException("Save or reload your changes before toggling presence.");
        await backend.LoadConfig();
        var config = backend.Config!.DeepClone().AsObject();
        config["presence_enabled"] = !B(config["presence_enabled"]);
        await backend.SaveConfig(config);
        Footer.Text = B(config["presence_enabled"]) ? "Publication enabled." : "Publication paused · local monitoring continues.";
        if (page == "Overview") RenderOverview();
    }

    public void ShowDashboard() { Show(); WindowState = WindowState.Normal; if (page == "Overview") RenderOverview(); Activate(); }
    private void ShowFromTray() => ShowDashboard();
    public void RequestExit() => _ = Guard(ExitApp);

    private void ApplyDesktopPreferences()
    {
        tray.Visible = !backend.Options.HideTrayIcon;
        if (backend.Options.StartWithChatGpt && !testMode) hostTimer.Start(); else hostTimer.Stop();
    }

    private async Task CheckHost()
    {
        var running = await Task.Run(HostMonitor.IsRunning);
        await ApplyHostState(running);
    }

    private async Task ObserveHost()
    {
        checkingHost = true;
        try { var running = await Task.Run(HostMonitor.IsRunning); if (!closing && !busy && !dirty && running != hostWasRunning) await Guard(() => ApplyHostState(running)); }
        finally { checkingHost = false; }
    }

    private async Task ApplyHostState(bool running)
    {
        if (running && !hostWasRunning && !backend.Running && terminalProcess is not { HasExited: false })
        {
            await StartEngine(); startedByHost = true;
        }
        else if (!running && startedByHost) await StopEngine();
        hostWasRunning = running;
    }

    private Task OpenTerminal() => OpenTerminal(backend.CreateTerminalStartInfo());

    private async Task OpenTerminal(ProcessStartInfo start)
    {
        if (dirty) throw new InvalidOperationException("Save or reload your changes before opening the terminal.");
        if (terminalProcess is { HasExited: false }) { Footer.Text = "The original terminal is already running."; return; }
        if (backend.Options.MonitorOnly && B(backend.Config?["presence_enabled"])) throw new InvalidOperationException("The original terminal uses the publication setting from Privacy. Pause publication first to keep it local.");
        var resume = backend.Running;
        resumeAfterTerminal = false;
        await backend.Stop();
        var child = new Process { StartInfo = start, EnableRaisingEvents = true };
        child.Exited += (_, _) => Dispatcher.BeginInvoke(() => CompleteTerminal(child, resume));
        try
        {
            if (!child.Start()) throw new IOException("Windows did not start the terminal process.");
            terminalProcess = child;
        }
        catch (Exception error)
        {
            child.Dispose();
            if (resume) await StartEngine();
            throw new InvalidOperationException("Could not open the original terminal: " + error.Message, error);
        }
        Footer.Text = "Original terminal opened. Press Q there to return to desktop monitoring.";
        if (page == "Overview") RenderOverview();
    }

    private void CompleteTerminal(Process child, bool resume)
    {
        if (!ReferenceEquals(terminalProcess, child)) return;
        terminalProcess = null;
        child.Dispose();
        resumeAfterTerminal = resume && !closing;
        if (page == "Overview" && IsVisible) RenderOverview();
        ResumeTerminalMonitoring();
    }

    private void ResumeTerminalMonitoring()
    {
        if (!resumeAfterTerminal || busy || dirty || closing) return;
        resumeAfterTerminal = false;
        _ = Guard(StartEngine);
    }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        e.Cancel = true;
        if (backend.Options.CloseToTray) { Hide(); return; }
        _ = Guard(ExitApp);
    }

    private async Task ExitApp()
    {
        if (dirty || editors.Values.Any(editor => editor.Dirty)) throw new InvalidOperationException("Save or reload the tabs with unsaved changes before exiting.");
        if (terminalProcess is { HasExited: false }) throw new InvalidOperationException("Press Q in the original terminal to close it before exiting this app.");
        await backend.Stop();
        closing = true;
        refreshTimer.Stop();
        hostTimer.Stop();
        tray.Dispose();
        Close();
        Application.Current.Shutdown();
    }

    private async Task SelfTest()
    {
        var evidence = Path.Combine(root, "Data", "Verification"); Directory.CreateDirectory(evidence);
        var checks = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("Verification failed: " + label); checks.Add(label); }
        Check(backend.Config != null && !File.Exists(backend.ConfigPath), "Default configuration loads without creating a file");
        var config = backend.Config!.DeepClone().AsObject();
        config["presence_enabled"] = false;
        await backend.SaveConfig(config);
        Check(!B(JsonNode.Parse(File.ReadAllText(backend.ConfigPath))?["presence_enabled"]), "Validated configuration saves atomically");
        var beforeBackup = File.ReadAllText(backend.ConfigPath);
        var backupFiles = Directory.GetFiles(backend.BackupDirectory, "*.json");
        var privacy = config["privacy"]!.AsObject(); privacy["show_project_name"] = false;
        await backend.SaveConfig(config);
        var createdBackup = Directory.GetFiles(backend.BackupDirectory, "*.json").Except(backupFiles).Single();
        Check(File.ReadAllText(createdBackup) == beforeBackup, "Replacement preserves an exact backup on the app drive");
        var modifiedBeforeNoOp = File.GetLastWriteTimeUtc(backend.ConfigPath);
        var backupsBeforeNoOp = Directory.GetFiles(backend.BackupDirectory, "*.json").Length;
        await backend.SaveConfig(config);
        Check(File.GetLastWriteTimeUtc(backend.ConfigPath) == modifiedBeforeNoOp && Directory.GetFiles(backend.BackupDirectory, "*.json").Length == backupsBeforeNoOp, "Saving unchanged settings avoids rewrites and redundant backups");
        File.AppendAllText(backend.ConfigPath, " ");
        var rejected = false;
        try { await backend.SaveConfig(config); } catch (IOException) { rejected = true; }
        Check(rejected, "Concurrent external settings edits are preserved");
        await backend.LoadConfig();
        var invalidRejected = false;
        try { await backend.Command("config-check", "{\"display\":{\"desktop_presence_design\":\"invalid\"}}"); }
        catch (InvalidOperationException) { invalidRejected = true; }
        Check(invalidRejected, "Invalid engine enums are rejected before writing");
        var configBeforeStart = File.ReadAllText(backend.ConfigPath);
        var modifiedBeforeStart = File.GetLastWriteTimeUtc(backend.ConfigPath);
        await StartEngine();
        Check(File.ReadAllText(backend.ConfigPath) == configBeforeStart && File.GetLastWriteTimeUtc(backend.ConfigPath) == modifiedBeforeStart, "Starting preserves the existing configuration without rewriting it");
        Check(backend.Snapshot != null, "Desktop bridge streams real parsed session snapshots");
        Check(S(backend.Snapshot?["discord_status"]) == "Monitoring only", "Verification cannot publish Discord activity");
        Check(backend.Snapshot?["sessions"] is JsonArray array && array.Count > 0, "Fixture sessions appear in dashboard");
        Check(S(backend.Snapshot?["sessions"]?[0]?["project_name"]) == "Verification chat title", "The project field reads the chat title from local metadata");
        Check(S(backend.Snapshot?["preview"]?["app_name"]) == "Codex App", "Authoritative desktop session identity is preserved");
        Check(N(backend.Snapshot?["metrics"]?["totals"]?["total_tokens"]) == 9200, "Parsed token totals reach the desktop dashboard");
        Check(backend.Snapshot?["limits"]?["windows"] is JsonArray windows && windows.Count == 2, "All observed quota windows reach the desktop dashboard");
        var duplicateRejected = false;
        try { await backend.Command("desktop-bridge --observe"); } catch (InvalidOperationException error) { duplicateRejected = error.Message.Contains("already running"); }
        Check(duplicateRejected && backend.Running, "Duplicate engines are rejected without terminating the original");
        var duplicate = new Backend(root, backend.Options, true);
        var startupRejected = false;
        try { await duplicate.Start(); } catch (IOException error) { startupRejected = error.Message.Contains("already running"); }
        Check(startupRejected && backend.Running, "Startup failures reach the dashboard instead of silently reporting success");
        Navigate("Overview"); UpdateLayout();
        var originalStart = overviewStart;
        var originalBuilds = overviewBuilds;
        var originalExpander = Elements<Expander>(PageBody).First();
        originalExpander.IsExpanded = true;
        PageScroll.ScrollToVerticalOffset(80); UpdateLayout();
        var originalOffset = PageScroll.VerticalOffset;
        var updated = backend.Snapshot!.DeepClone().AsObject();
        updated["metrics"]!["totals"]!["total_tokens"] = 9300;
        RenderOverview(updated); UpdateLayout();
        Check(ReferenceEquals(originalStart, overviewStart) && overviewBuilds == originalBuilds, "Live numeric updates reuse controls without rebuilding the dashboard");
        Check(Elements<TextBlock>(PageBody).Any(text => text.Text == "9,300"), "Live values update immediately in retained controls");
        Check(originalExpander.IsExpanded && Math.Abs(PageScroll.VerticalOffset - originalOffset) < 1, "Live updates preserve expansion and scroll position");
        await RunInBackground();
        Check(!IsVisible && backend.Running, "Background action hides the dashboard and keeps the engine running");
        var hiddenSnapshot = displayedSnapshot;
        await Task.Delay(2200);
        Check(ReferenceEquals(hiddenSnapshot, displayedSnapshot) && backend.Running, "Hidden dashboard suspends rendering while monitoring continues");
        ShowDashboard();
        Check(IsVisible && backend.Running && ReferenceEquals(displayedSnapshot, backend.Snapshot), "Reopening restores the dashboard with the latest live snapshot");
        backend.Options.HideTrayIcon = true; ApplyDesktopPreferences();
        await RunInBackground();
        Check(!tray.Visible && !IsVisible && backend.Running, "The dashboard and tray icon can both be hidden while presence keeps running");
        ShowDashboard();
        Check(IsVisible && !tray.Visible && backend.Running, "A dashboard with a hidden tray icon can still be restored");
        backend.Options.HideTrayIcon = false; ApplyDesktopPreferences();
        Check(!refreshTimer.IsEnabled, "Unchanged snapshots do not keep an idle UI refresh timer running");
        Navigate("Layout"); UpdateLayout();
        var projectCheckbox = Elements<CheckBox>(PageBody).Single(item => item.Content?.ToString() == "Chat title");
        projectCheckbox.IsChecked = true;
        await SaveDraft();
        Check(B(backend.Config?["privacy"]?["show_project_name"]) && backend.Config?["display"]?["presence_layout"]?["fields"] is JsonArray enabledFields && enabledFields.Any(field => S(field?["field"]) == "project" && B(field?["enabled"])), "Layout visibility control saves and stays synchronized with privacy");
        var identityCombo = Elements<ComboBox>(PageBody).Single(item => AutomationProperties.GetName(item) == "Discord application");
        identityCombo.SelectedValue = "chat_gpt_app";
        await SaveDraft();
        Check(S(backend.Config?["display"]?["desktop_presence_design"]) == "chat_gpt_app", "Desktop identity dropdown saves its engine value");
        var tokenLabel = Elements<TextBox>(PageBody).Single(item => AutomationProperties.GetName(item) == "Token label"); tokenLabel.Text = "TOKENS";
        var contextLabel = Elements<TextBox>(PageBody).Single(item => AutomationProperties.GetName(item) == "Context label"); contextLabel.Text = "WINDOW";
        var customText = Elements<TextBox>(PageBody).Single(item => AutomationProperties.GetName(item) == "Custom text"); customText.Text = "Building something useful";
        var customCheckbox = Elements<CheckBox>(PageBody).Single(item => item.Content?.ToString() == "Custom text"); customCheckbox.IsChecked = true;
        await SaveDraft();
        Check(S(backend.Config?["display"]?["token_label"]) == "TOKENS" && S(backend.Config?["display"]?["context_label"]) == "WINDOW", "Custom token and context labels save through the engine validator");
        Check(B(backend.Config?["privacy"]?["show_custom_text"]) && S(backend.Config?["display"]?["custom_text"]) == "Building something useful", "Custom presence text and its visibility save together");
        Navigate("Settings"); UpdateLayout();
        Check(Elements<CheckBox>(PageBody).Any(item => AutomationProperties.GetName(item) == "Open in background and start presence"), "Background launch preference is available in settings");
        Check(Elements<CheckBox>(PageBody).Any(item => AutomationProperties.GetName(item) == "Hide the system tray icon") && Elements<CheckBox>(PageBody).Any(item => AutomationProperties.GetName(item) == "Start with Codex / ChatGPT in background"), "Hidden-tray and background host launch preferences are available");
        var subscription = Elements<CheckBox>(PageBody).Single(item => AutomationProperties.GetName(item) == "Show subscription in Discord"); subscription.IsChecked = false;
        await SaveDraft();
        Check(!B(backend.Config?["privacy"]?["show_subscription"]), "Subscription visibility saves independently from the plan price");
        var timer = Elements<ComboBox>(PageBody).Single(item => AutomationProperties.GetName(item) == "Presence timer");
        timer.SelectedValue = "continuous";
        Navigate("Layout"); UpdateLayout();
        Check(page == "Layout" && editors["Settings"].Dirty, "Tabs remain accessible while unsaved settings are retained");
        var retainedLabel = Elements<TextBox>(PageBody).Single(item => AutomationProperties.GetName(item) == "Token label"); retainedLabel.Text = "Token";
        await SaveDraft();
        Navigate("Settings"); UpdateLayout();
        Check(ReferenceEquals(timer, Elements<ComboBox>(PageBody).Single(item => AutomationProperties.GetName(item) == "Presence timer")) && S(timer.SelectedValue == null ? null : JsonValue.Create(timer.SelectedValue.ToString())) == "continuous", "Returning to a tab preserves its controls and unsaved selection");
        await SaveDraft();
        Check(S(backend.Config?["display"]?["timer_mode"]) == "continuous" && S(backend.Config?["display"]?["token_label"]) == "Token", "Saving a retained draft preserves changes already saved on another tab");
        Navigate("Layout"); UpdateLayout();
        var separateUsage = Elements<CheckBox>(PageBody).Single(item => AutomationProperties.GetName(item) == "Tokens and context on a separate line"); separateUsage.IsChecked = true;
        await SaveDraft();
        Check(B(backend.Config?["display"]?["separate_usage_line"]), "The separate usage line preference saves through the runtime");
        Navigate("Settings"); UpdateLayout();
        var engineBeforeAppearance = backend.ProcessId;
        Elements<ComboBox>(PageBody).Single(item => AutomationProperties.GetName(item) == "Interface style").SelectedValue = "soft";
        Elements<ComboBox>(PageBody).Single(item => AutomationProperties.GetName(item) == "Color mode").SelectedValue = "light";
        await SaveDraft();
        var savedAppearance = Preferences.Load(Path.Combine(root, "Data", "Verification", "Preferences"));
        Check(savedAppearance.InterfaceStyle == "soft" && savedAppearance.ColorMode == "light", "Interface style and color mode persist independently");
        Check(backend.ProcessId == engineBeforeAppearance && backend.Running, "Saving appearance retains the active presence engine and connection");
        var pollBox = Elements<TextBox>(PageBody).Single(item => AutomationProperties.GetName(item) == "Poll interval (seconds)");
        pollBox.Text = "0";
        var beforeValidation = File.ReadAllText(backend.ConfigPath);
        var numericRejected = false;
        try { await SaveDraft(); } catch (InvalidOperationException) { numericRejected = true; }
        Check(numericRejected && File.ReadAllText(backend.ConfigPath) == beforeValidation, "Invalid monitoring values do not write any configuration");
        dirty = false;
        Navigate("Tools"); UpdateLayout();
        Check(Elements<Button>(PageBody).Any(item => AutomationProperties.GetName(item) == "Open original terminal"), "The original terminal launcher is available in Tools");
        var terminalRejected = false;
        try { await backend.Command("terminal-view"); } catch (InvalidOperationException error) { terminalRejected = error.Message.Contains("already running"); }
        Check(terminalRejected && backend.Running, "Opening a terminal cannot take over an existing presence engine");
        var failedLaunch = backend.CreateTerminalStartInfo();
        failedLaunch.FileName = Path.Combine(evidence, "missing-terminal.exe");
        var failedSafely = false;
        try { await OpenTerminal(failedLaunch); } catch (InvalidOperationException error) { failedSafely = error.Message.StartsWith("Could not open the original terminal:"); }
        UpdateOverviewControls();
        Check(failedSafely && terminalProcess == null && backend.Running, "A failed terminal launch restores monitoring and leaves no unstarted process behind");
        var terminalStart = backend.CreateTerminalStartInfo();
        terminalStart.WindowStyle = ProcessWindowStyle.Hidden;
        await OpenTerminal(terminalStart);
        var launchedTerminal = terminalProcess!;
        var terminalPid = launchedTerminal.Id;
        var terminalStatus = "";
        for (var attempt = 0; attempt < 25 && !terminalStatus.Contains("pid: " + terminalPid); attempt++)
        {
            await Task.Delay(200);
            terminalStatus = await backend.Command("status");
        }
        Check(!launchedTerminal.HasExited && !backend.Running && terminalStatus.Contains("pid: " + terminalPid), "The original terminal opens successfully and owns the runtime after a failed launch");
        busy = false;
        launchedTerminal.Kill();
        await launchedTerminal.WaitForExitAsync();
        for (var attempt = 0; attempt < 40 && (terminalProcess != null || !backend.Running || busy); attempt++) await Task.Delay(150);
        busy = true;
        Check(terminalProcess == null && backend.Running && !resumeAfterTerminal, "Exiting the terminal automatically restores desktop monitoring");
        await StopEngine();
        Hide();
        await ApplyHostState(true);
        Check(backend.Running && startedByHost && !IsVisible, "A host launch starts presence in the background without showing the dashboard");
        await ApplyHostState(false);
        Check(!backend.Running && !startedByHost, "Presence started by ChatGPT stops when the host closes");
        await StartEngine();
        await ApplyHostState(true); await ApplyHostState(false);
        Check(backend.Running, "Closing ChatGPT preserves an engine that was started manually");
        ShowDashboard();
        Check(StatusPanel.Child is Grid status && status.Children.Contains(Footer) && Footer.FontSize >= 13, "Status text lives inside a readable dedicated panel");
        Check(!Elements<TextBlock>(this).Any(text => text.Text == "/ desktop"), "The title bar has no redundant desktop suffix");
        var shortcutPath = DesktopShortcut.Create(root, Path.Combine(evidence, "Desktop"));
        dynamic shortcutShell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic shortcutLink = shortcutShell.CreateShortcut(shortcutPath);
        Check(File.Exists(shortcutPath) && (string)shortcutLink.TargetPath == Path.Combine(root, "Codex Presence.exe") && (string)shortcutLink.Arguments == "", "A Windows desktop shortcut opens the dashboard without startup flags");
        Marshal.FinalReleaseComObject(shortcutLink); Marshal.FinalReleaseComObject(shortcutShell);
        void Capture(string name)
        {
            UpdateLayout();
            var image = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(evidence, name + ".png")); encoder.Save(file);
        }
        double Luminance(string key)
        {
            var color = ((SolidColorBrush)FindResource(key)).Color;
            double Linear(byte component) { var value = component / 255d; return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4); }
            return Linear(color.R) * 0.2126 + Linear(color.G) * 0.7152 + Linear(color.B) * 0.0722;
        }
        foreach (var style in new[] { "minimal", "soft", "chatgpt", "paper", "studio" })
        foreach (var mode in new[] { "dark", "light" })
        {
            backend.Options.InterfaceStyle = style; backend.Options.ColorMode = mode;
            Appearance.Apply(this, backend.Options); Navigate("Overview"); UpdateLayout();
            var foreground = Luminance("Muted"); var background = Luminance("Surface");
            Check((Math.Max(foreground, background) + 0.05) / (Math.Min(foreground, background) + 0.05) >= 4.5, style + " / " + mode + " keeps secondary text readable");
            Check(PageBody.ActualWidth <= PageScroll.ActualWidth && StatusPanel.ActualWidth < ActualWidth && Elements<Button>(PageBody).All(button => button.DesiredSize.Width <= PageBody.ActualWidth), style + " / " + mode + " fits the compact window");
            Capture("Theme-" + style + "-" + mode);
        }
        backend.Options.InterfaceStyle = "minimal"; backend.Options.ColorMode = "dark"; Appearance.Apply(this, backend.Options);
        foreach (var name in navigation.Keys)
        {
            dirty = false; Navigate(name); UpdateLayout();
            Check(PageBody.Children.Count > 0, name + " page builds");
            Capture(name);
        }
        dirty = false;
        await backend.Stop();
        Check(!backend.Running, "Quit command gracefully stops the owned engine");
        File.WriteAllText(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new { passed = checks.Count, configVolume = Path.GetPathRoot(backend.ConfigPath), backupVolume = Path.GetPathRoot(root), checks, snapshot = backend.Snapshot }, Backend.JsonOptions));
        await ExitApp();
    }

    private async Task VerifyLive()
    {
        var configBeforeStart = File.Exists(backend.ConfigPath) ? File.ReadAllText(backend.ConfigPath) : null;
        if (!backend.Running) await StartEngine();
        for (var attempt = 0; attempt < 20 && !S(backend.Snapshot?["discord_status"]).StartsWith("Connected", StringComparison.OrdinalIgnoreCase); attempt++) await Task.Delay(500);
        var evidence = Path.Combine(root, "Data", "Verification");
        Directory.CreateDirectory(evidence);
        var connected = S(backend.Snapshot?["discord_status"]).StartsWith("Connected", StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(Path.Combine(evidence, "live-results.json"), JsonSerializer.Serialize(new { connected, status = S(backend.Snapshot?["discord_status"]), running = backend.Running, hidden = !IsVisible, everShown, trayVisible = tray.Visible, configUnchangedOnStart = configBeforeStart != null && File.ReadAllText(backend.ConfigPath) == configBeforeStart, application = S(backend.Snapshot?["preview"]?["app_name"]), sessions = backend.Snapshot?["sessions"]?.AsArray().Count ?? 0 }, Backend.JsonOptions));
        Footer.Text = connected ? "Connected to Discord · presence is live." : "Engine started · " + S(backend.Snapshot?["discord_status"]);
        if (!args.Contains("--verify-background")) ShowDashboard();
    }

    private static IEnumerable<T> Elements<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Elements<T>(child)) yield return descendant;
        }
    }
}
