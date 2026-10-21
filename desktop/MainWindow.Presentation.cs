using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CodexPresence;

public partial class MainWindow
{
    private bool windowResizing;
    private WindowPresentationState windowPresentation = new();
    private DispatcherTimer? presentationTimer;
    private DispatcherTimer? positionTimer;
    private Border? toast;
    private readonly Dictionary<string, System.Drawing.Icon> trayVariants = new();
    private string trayVariant = "";
    private readonly Dictionary<string, List<Action<JsonObject?>>> previewCallbacks = new();
    private List<Action<JsonObject?>> editorPreviewUpdates => previewCallbacks.TryGetValue(page, out var callbacks) ? callbacks : previewCallbacks[page] = new();
    private string? visualStateOverride;
    private Button? pendingAction;
    private sealed record EngineVisual(string Headline, string Detail, string Tone);

    private void InitializePresentation()
    {
        windowPresentation = testMode ? new() : WindowPresentationState.Load(root);
        SetValue(Appearance.ThemeModeProperty, windowPresentation.ThemeMode == "system" ? "system" : "inherit");
        var work = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, Math.Max(320, work.Width));
        MinHeight = Math.Min(MinHeight, Math.Max(420, work.Height));
        Width = double.IsFinite(windowPresentation.Width) ? Math.Clamp(windowPresentation.Width, MinWidth, Math.Max(MinWidth, work.Width)) : 640;
        Height = double.IsFinite(windowPresentation.Height) ? Math.Clamp(windowPresentation.Height, MinHeight, Math.Max(MinHeight, work.Height)) : 820;
        if (windowPresentation.Left is { } left && windowPresentation.Top is { } top && double.IsFinite(left) && double.IsFinite(top))
        {
            Left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - Width));
            Top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - Height));
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        SourceInitialized += (_, _) =>
        {
            Appearance.Apply(this, backend.Options);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(ResizeWindowMessage);
        };
        SystemParameters.StaticPropertyChanged += SystemPresentationChanged;
        Closed += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged -= SystemPresentationChanged;
            presentationTimer?.Stop(); positionTimer?.Stop();
            foreach (var icon in trayVariants.Values) icon.Dispose();
        };
        positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        positionTimer.Tick += async (_, _) => { positionTimer.Stop(); await SaveWindowPresentation(); };
        SizeChanged += (_, _) => { QueueWindowPresentationSave(); };
        LocationChanged += (_, _) => { QueueWindowPresentationSave(); };
        presentationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        presentationTimer.Tick += (_, _) =>
        {
            if (!IsVisible || windowResizing) return;
            UpdateFooterPresentation();
            if (page is "Privacy" or "Layout") foreach (var update in editorPreviewUpdates) update(backend.Snapshot);
        };
        presentationTimer.Start();
        IsVisibleChanged += (_, _) => { if (IsVisible && !testMode) _ = EnsureWebInterface(); };
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(AnimateToggle), true);
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(AnimateToggle), true);
        PreviewKeyDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && navigation.Values.Any(button => ReferenceEquals(button, source) || button.IsKeyboardFocusWithin) && e.Key is Key.Left or Key.Right or Key.Home or Key.End)
            {
                var names = navigation.Keys.ToArray(); var index = Array.IndexOf(names, page);
                var next = e.Key switch { Key.Home => 0, Key.End => names.Length - 1, Key.Left => (index + names.Length - 1) % names.Length, _ => (index + 1) % names.Length };
                NavigateFromKeyboard(names[next]); e.Handled = true;
            }
        };
    }

    private void QueueWindowPresentationSave()
    {
        if (!initialized || testMode || windowResizing || positionTimer == null) return;
        positionTimer.Stop();
        positionTimer.Start();
    }

    private IntPtr ResizeWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0024 && lParam != IntPtr.Zero)
        {
            var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
            var work = screen.WorkingArea; var monitor = screen.Bounds;
            var limits = Marshal.PtrToStructure<NativeWindowLimits>(lParam);
            var dpi = VisualTreeHelper.GetDpi(this);
            limits.MaxPosition = new NativeWindowPoint { X = work.Left - monitor.Left, Y = work.Top - monitor.Top };
            limits.MaxSize = new NativeWindowPoint { X = work.Width, Y = work.Height };
            limits.MinTrackSize = new NativeWindowPoint { X = (int)Math.Ceiling(MinWidth * dpi.DpiScaleX), Y = (int)Math.Ceiling(MinHeight * dpi.DpiScaleY) };
            Marshal.StructureToPtr(limits, lParam, false);
            handled = true;
        }
        else if (message == 0x0231)
        {
            windowResizing = true;
            refreshTimer.Stop();
            positionTimer?.Stop();
            if (webReady) webInterface?.CoreWebView2?.PostWebMessageAsJson("{\"type\":\"resizing\",\"active\":true}");
        }
        else if (message == 0x0232)
        {
            windowResizing = false;
            QueueWindowPresentationSave();
            if (webReady) webInterface?.CoreWebView2?.PostWebMessageAsJson("{\"type\":\"resizing\",\"active\":false}");
            if (IsVisible && page == "Overview") refreshTimer.Start();
            if (webReady) SendWebState();
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowPoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowLimits
    {
        public NativeWindowPoint Reserved;
        public NativeWindowPoint MaxSize;
        public NativeWindowPoint MaxPosition;
        public NativeWindowPoint MinTrackSize;
        public NativeWindowPoint MaxTrackSize;
    }

    private void SystemPresentationChanged(object? sender, PropertyChangedEventArgs e)
        => Dispatcher.BeginInvoke(() => Appearance.Apply(this, backend.Options));

    private void AnimateToggle(object sender, RoutedEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation || testMode || e.OriginalSource is not ToggleButton toggle || !toggle.IsLoaded) return;
        var duration = TimeSpan.FromMilliseconds((double)FindResource("MotionMilliseconds"));
        if (toggle.Template.FindName("Thumb", toggle) is Ellipse thumb)
        {
            var transform = new TranslateTransform(); thumb.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(toggle.IsChecked == true ? -20 : 20, 0, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }
        else if (toggle.Template.FindName("Chevron", toggle) is System.Windows.Shapes.Path chevron && chevron.RenderTransform is RotateTransform rotation)
            rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(toggle.IsChecked == true ? -90 : 0, toggle.IsChecked == true ? 0 : -90, duration));
    }

    private Task SaveWindowPresentation()
    {
        if (testMode) return Task.CompletedTask;
        windowPresentation.LastTab = page;
        if (!double.IsFinite(Left) || !double.IsFinite(Top) || ActualWidth <= 0 || ActualHeight <= 0) return windowPresentation.Save(root);
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        windowPresentation.Width = bounds.Width; windowPresentation.Height = bounds.Height;
        windowPresentation.Left = bounds.Left; windowPresentation.Top = bounds.Top;
        return windowPresentation.Save(root);
    }

    private void SetPresentationTheme(string mode)
    {
        windowPresentation.ThemeMode = mode;
        SetValue(Appearance.ThemeModeProperty, mode);
        if (!testMode) _ = windowPresentation.Save(root);
    }

    private void FooterDetailsClick(object sender, RoutedEventArgs e) => Navigate("Tools");

    private static string SettingDescription(string key) => key switch
    {
        "presence_enabled" => "Allow the engine to publish activity to Discord.",
        "enabled" => "Keep session details private while monitoring locally.",
        "show_project_name" => "Include the current chat title or project name.",
        "show_git_branch" => "Include your current Git branch.",
        "show_activity" => "Show what Codex is doing right now.",
        "show_activity_target" => "Include file names or command targets.",
        "show_model" => "Include the model used by this session.",
        "show_reasoning_effort" => "Include the selected reasoning effort.",
        "show_tokens" => "Include the token usage reported by Codex.",
        "show_context_window" => "Include the session’s context usage.",
        "show_subscription" => "Include your subscription name in Discord.",
        "show_custom_text" => "Include your custom text in the configured layout.",
        "StartWithChatGpt" => "Watch quietly for the Codex or ChatGPT desktop app.",
        "StartWithWindows" => "Start hidden when you sign in to Windows.",
        "StartEngineOnOpen" => "Begin monitoring when this companion opens.",
        "StartInBackground" => "Keep the dashboard hidden on launch.",
        "CloseToTray" => "Keep monitoring after the dashboard closes.",
        "HideTrayIcon" => "Reopen the desktop shortcut to return to the dashboard.",
        "PreferOtherApps" => "Clear Codex presence while a priority app is running.",
        "DetectSteamGames" => "Recognize running games using Steam’s local status.",
        "MonitorOnly" => "Read local sessions without publishing activity.",
        "EfficiencyMode" => "Request lower background priority from Windows.",
        _ => ""
    };

    private Grid ToolRow(string title, string description, Button action)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Margin = new Thickness(0, 0, 16, 0) }; var heading = Text(title, 14); heading.FontWeight = FontWeights.SemiBold; text.Children.Add(heading); var detail = Text(description, 12, Muted); detail.Margin = new Thickness(0, 4, 0, 0); text.Children.Add(detail); row.Children.Add(text);
        Grid.SetColumn(action, 1); action.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(action); return row;
    }

    private EngineVisual EnginePresentation(JsonObject? snapshot)
    {
        var status = S(snapshot?["discord_status"], "Connecting to Discord…");
        var state = visualStateOverride ?? (!backend.Running ? backend.LastError == null ? "stopped" : "error" : backend.Starting && snapshot == null ? "loading" : !backend.PublicationEnabled ? "paused" : B(snapshot?["startup_waiting"]) ? "idle" : status.StartsWith("Connected", StringComparison.OrdinalIgnoreCase) ? "live" : status.Contains("error", StringComparison.OrdinalIgnoreCase) || status.Contains("failed", StringComparison.OrdinalIgnoreCase) ? "error" : "waiting");
        return state switch
        {
            "live" => new(B(snapshot?["manual_mode"]) ? "Manual presence is live" : "Presence is live", "Connected to Discord", "Success"),
            "paused" => new("Presence paused", "Local session monitoring continues", "Warning"),
            "stopped" => new("Engine stopped", "Start the engine to watch for Codex activity", "Neutral"),
            "loading" => new("Reading your session", "Starting the presence engine…", "Accent"),
            "error" => new("Connection needs attention", "Open Tools for details and recovery options", "Danger"),
            "idle" => new("Ready when you are", "Waiting for new Codex activity", "Neutral"),
            _ when B(snapshot?["priority_held"]) => new("Another app has priority", "Codex monitoring continues in the background", "Warning"),
            _ when B(snapshot?["idle_timeout_hidden"]) => new("Presence hidden", "Your inactivity timer has elapsed", "Neutral"),
            _ when B(snapshot?["monitoring_only"]) => new("Monitoring locally", "Discord publication is switched off", "Neutral"),
            _ => new("Waiting for Discord", "Open the Discord desktop app. We’ll reconnect automatically.", "Warning")
        };
    }

    private Border Surface(UIElement child, string background = "Surface")
    {
        var border = new Border { Child = child };
        border.SetResourceReference(Border.BackgroundProperty, background);
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        border.SetResourceReference(Border.BorderThicknessProperty, "CardBorder");
        border.SetResourceReference(Border.CornerRadiusProperty, "CardRadius");
        border.SetResourceReference(Border.PaddingProperty, "CardPadding");
        border.SetResourceReference(Border.MarginProperty, "CardSpacing");
        return border;
    }

    private TextBlock SectionLabel(string title)
    {
        var label = Text(title, 12, Muted);
        label.FontWeight = FontWeights.SemiBold;
        label.Margin = new Thickness(0, 12, 0, 12);
        return label;
    }

    private void BuildEngineControlCard()
    {
        var stack = new Grid();
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); header.ColumnDefinitions.Add(new ColumnDefinition());
        var dot = new Ellipse { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) }; header.Children.Add(dot);
        var text = new StackPanel(); Grid.SetColumn(text, 1); header.Children.Add(text);
        var headline = Text("", 18); headline.FontWeight = FontWeights.SemiBold; text.Children.Add(headline);
        var detail = Text("", 12, Muted); detail.Margin = new Thickness(0, 4, 0, 0); text.Children.Add(detail);
        stack.Children.Add(header);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) }; Grid.SetRow(controls, 1);
        overviewPause = AsyncButton("Pause", TogglePresence);
        overviewStart = AsyncButton("Start engine", () => backend.Running ? StopEngine() : StartEngine());
        overviewBackground = ActionButton("⋯", () => { overviewBackground!.ContextMenu!.IsOpen = true; });
        overviewBackground.MinWidth = 36; overviewBackground.Padding = new Thickness(8, 0, 8, 0); overviewBackground.FontSize = 20;
        overviewBackground.Style = (Style)FindResource("GhostButton");
        overviewBackground.ToolTip = "More actions · background and exit"; AutomationProperties.SetName(overviewBackground, "More actions");
        var menu = new ContextMenu { PlacementTarget = overviewBackground, Placement = PlacementMode.Bottom };
        menu.Items.Add(MenuAction("Run in background", "\uE8A7", RunInBackground));
        menu.Items.Add(new Separator());
        var exit = MenuAction("Exit", "\uE8BB", ExitApp); exit.SetResourceReference(ForegroundProperty, "Danger"); menu.Items.Add(exit);
        overviewBackground.ContextMenu = menu;
        foreach (var button in new[] { overviewPause, overviewStart, overviewBackground }) { button.Margin = new Thickness(8, 0, 0, 0); controls.Children.Add(button); }
        stack.Children.Add(controls);
        overviewUpdates.Add(snapshot =>
        {
            var state = EnginePresentation(snapshot); headline.Text = state.Headline; detail.Text = state.Detail;
            dot.SetResourceReference(Shape.FillProperty, state.Tone);
            AutomationProperties.SetName(header, "Presence engine, " + state.Headline.ToLowerInvariant());
            AutomationProperties.SetLiveSetting(headline, AutomationLiveSetting.Polite);
        });
        PageBody.Children.Add(Surface(stack));
    }

    private MenuItem MenuAction(string title, string glyph, Func<Task> action)
    {
        var item = new MenuItem { Header = title, Icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 16 } };
        item.Click += (_, _) => _ = Guard(action);
        return item;
    }

    private Border DiscordPreviewCard(ICollection<Action<JsonObject?>> updates, bool editor = false)
    {
        var body = new StackPanel();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var badge = new Border { Child = Text("Preview", 12, Muted), Padding = new Thickness(8, 4, 8, 4) };
        badge.SetResourceReference(Border.BackgroundProperty, "Control");
        badge.SetResourceReference(Border.CornerRadiusProperty, "BadgeRadius");
        DockPanel.SetDock(badge, Dock.Right); top.Children.Add(badge); top.Children.Add(Text("Playing", 12, Muted)); body.Children.Add(top);
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        var tile = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top, Child = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo-256.png")) } };
        row.Children.Add(tile);
        var lines = new StackPanel(); Grid.SetColumn(lines, 1); row.Children.Add(lines);
        var title = Text("Codex App", 14); title.FontWeight = FontWeights.SemiBold;
        var details = Text("", 13); details.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        var state = Text("", 13); state.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        lines.Children.Add(title); lines.Children.Add(details); lines.Children.Add(state); body.Children.Add(row);
        var privacyHint = Text("Hidden by Privacy settings", 12, Muted); lines.Children.Add(privacyHint);
        var hidden = Text("Nothing to show yet · send a new Codex prompt", 13, Muted); hidden.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(hidden);
        var editorNote = Text("Saved Discord output · save changes to update this preview", 12, Muted); editorNote.Margin = new Thickness(0, 12, 0, 0); editorNote.Visibility = editor ? Visibility.Visible : Visibility.Collapsed; body.Children.Add(editorNote);
        var card = Surface(body, "DiscordCard"); card.Tag = "DiscordPreview";
        updates.Add(snapshot =>
        {
            var preview = snapshot?["preview"];
            title.Text = S(preview?["app_name"], "Codex App");
            details.Text = S(preview?["details"], ""); state.Text = S(preview?["state"], "");
            privacyHint.Visibility = (string.IsNullOrWhiteSpace(details.Text) || string.IsNullOrWhiteSpace(state.Text)) && backend.Config?["privacy"] is JsonObject privacy && (B(privacy["enabled"]) || privacy.Any(pair => pair.Key.StartsWith("show_", StringComparison.Ordinal) && pair.Value is JsonValue field && field.TryGetValue<bool>(out var visible) && !visible)) ? Visibility.Visible : Visibility.Collapsed;
            var empty = preview == null || B(snapshot?["startup_waiting"]) || (!B(snapshot?["manual_mode"]) && (snapshot?["sessions"] is not JsonArray sessions || sessions.Count == 0)) || visualStateOverride is "idle" or "loading" or "stopped";
            row.Visibility = empty ? Visibility.Collapsed : Visibility.Visible; hidden.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            hidden.Text = visualStateOverride == "loading" ? "Reading Codex sessions…" : "Nothing to show yet · send a new Codex prompt";
            AutomationProperties.SetName(card, "Discord preview, " + (empty ? hidden.Text : title.Text + ", " + details.Text + ", " + state.Text));
        });
        return card;
    }

    private void AddEditorPreview()
    {
        editorPreviewUpdates.Clear();
        var card = DiscordPreviewCard(editorPreviewUpdates, true);
        if (page == "Layout") { PinnedPreviewHost.Child = card; PinnedPreviewHost.Visibility = Visibility.Visible; }
        else PageBody.Children.Add(card);
        foreach (var update in editorPreviewUpdates) update(backend.Snapshot);
    }

    private static string CompactNumber(JsonNode? node)
    {
        if (Number(node) is not { } number) return "—";
        var magnitude = Math.Abs(number);
        return magnitude >= 1_000_000_000 ? (number / 1_000_000_000).ToString("0.#") + "B" : magnitude >= 1_000_000 ? (number / 1_000_000).ToString("0.#") + "M" : magnitude >= 100_000 ? (number / 1_000).ToString("0.#") + "K" : number.ToString("N0");
    }

    private JsonNode? ActiveContext(JsonObject? snapshot)
    {
        var context = (snapshot?["manual_session"] ?? snapshot?["sessions"]?.AsArray().FirstOrDefault(session => S(session?["session_id"]) == S(snapshot?["active_session_id"])))?["context_window"];
        return Number(context?["remaining_percent"]) is { } remaining ? JsonValue.Create(Math.Clamp(100 - remaining, 0, 100)) : null;
    }

    private void BuildSessionUsage()
    {
        PageBody.Children.Add(SectionLabel("Session usage"));
        var tiles = new UniformGrid { Columns = 3 };
        tiles.Children.Add(StatTile("Tokens", s => CompactNumber(s?["metrics"]?["totals"]?["total_tokens"]), null, s => Count(s?["metrics"]?["totals"]?["total_tokens"])));
        tiles.Children.Add(StatTile("Cache hit", s => Number(s?["metrics"]?["totals"]?["cache_hit_ratio"]) is { } ratio ? (Math.Clamp(ratio, 0, 1) * 100).ToString("F1") + "%" : "—", s => Number(s?["metrics"]?["totals"]?["cache_hit_ratio"]) * 100));
        tiles.Children.Add(StatTile("Context used", s => Number(ActiveContext(s)) is { } used ? used.ToString("0.#") + "%" : "—", s => Number(ActiveContext(s)), warning: true));
        var statistics = Surface(tiles); statistics.Padding = new Thickness(0);
        ((Border)tiles.Children[tiles.Children.Count - 1]).BorderThickness = new Thickness(0);
        PageBody.Children.Add(statistics);
        var usage = new StackPanel();
        LiveRow(usage, "Input / cached / output", s => CompactNumber(s?["metrics"]?["totals"]?["input_tokens"]) + " / " + CompactNumber(s?["metrics"]?["totals"]?["cached_input_tokens"]) + " / " + CompactNumber(s?["metrics"]?["totals"]?["output_tokens"]));
        usage.Children.Add(UsageStackedBar());
        LiveRow(usage, "Known cost ⓘ", s => Cost(s?["metrics"]?["totals"]?["known_cost_usd"], s?["metrics"]?["totals"]?["pricing_status"]));
        usage.Children[usage.Children.Count - 1].SetValue(ToolTipProperty, "Cost is unavailable when the local session does not contain enough pricing or attribution data. No estimate is invented.");
        LiveRow(usage, "Plan", s => S(s?["plan"])); LiveRow(usage, "Speed", s => Speed(s?["speed"]));
        usage.Children.Add(LiveJsonViewer("Plan and speed details", s => new JsonObject { ["plan"] = s?["plan_details"]?.DeepClone(), ["speed"] = s?["service_tier"]?.DeepClone() }));
        PageBody.Children.Add(Surface(usage));
    }

    private Border StatTile(string label, Func<JsonObject?, string> value, Func<JsonObject?, double?>? progress, Func<JsonObject?, string>? exact = null, bool warning = false)
    {
        var panel = new StackPanel(); panel.Children.Add(Text(label, 12, Muted));
        var number = LiveText(value, 22); number.FontWeight = FontWeights.SemiBold; number.Margin = new Thickness(0, 4, 0, 8); number.TextWrapping = TextWrapping.NoWrap; number.TextTrimming = TextTrimming.CharacterEllipsis; panel.Children.Add(number);
        if (progress != null)
        {
            var bar = new ProgressBar { Maximum = 100, Height = 4 };
            overviewUpdates.Add(snapshot => { var next = progress(snapshot); bar.Value = Math.Clamp(next ?? 0, 0, 100); bar.SetResourceReference(ForegroundProperty, warning && next >= 80 ? "Warning" : "Accent"); AutomationProperties.SetName(bar, label + ", " + value(snapshot)); }); panel.Children.Add(bar);
        }
        overviewUpdates.Add(snapshot => { var full = exact?.Invoke(snapshot) ?? value(snapshot); number.ToolTip = full; AutomationProperties.SetName(number, label + ", " + full); });
        var copy = new ContextMenu(); var item = new MenuItem { Header = "Copy full value" }; item.Click += (_, _) => { Clipboard.SetText(exact?.Invoke(displayedSnapshot) ?? value(displayedSnapshot)); ShowToast("Copied"); }; copy.Items.Add(item); number.ContextMenu = copy;
        var card = new Border { Child = panel, Padding = new Thickness(16), BorderThickness = new Thickness(0, 0, 1, 0) };
        card.SetResourceReference(Border.BorderBrushProperty, "Line");
        return card;
    }

    private StackPanel UsageStackedBar()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 12) };
        var grid = new Grid { Height = 6 }; panel.Children.Add(grid);
        foreach (var key in new[] { "DataInput", "DataCached", "DataOutput" }) { grid.ColumnDefinitions.Add(new ColumnDefinition()); var fill = new Border(); fill.SetResourceReference(Border.BackgroundProperty, key); Grid.SetColumn(fill, grid.Children.Count); grid.Children.Add(fill); }
        var legend = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var pair in new[] { ("Input", "DataInput"), ("Cached", "DataCached"), ("Output", "DataOutput") })
        {
            var entry = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) }; var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 8, 0) }; dot.SetResourceReference(Shape.FillProperty, pair.Item2); entry.Children.Add(dot); entry.Children.Add(Text(pair.Item1, 12, Muted)); legend.Children.Add(entry);
        }
        panel.Children.Add(legend);
        overviewUpdates.Add(snapshot =>
        {
            var totals = snapshot?["metrics"]?["totals"]; var input = Math.Max(0, N(totals?["input_tokens"])); var cached = Math.Clamp(N(totals?["cached_input_tokens"]), 0, input); var output = Math.Max(0, N(totals?["output_tokens"]));
            var values = new[] { input - cached, cached, output };
            grid.Visibility = input + output > 0 ? Visibility.Visible : Visibility.Hidden;
            for (var index = 0; index < values.Length; index++) grid.ColumnDefinitions[index].Width = new GridLength(Math.Max(.0001, values[index]), GridUnitType.Star);
            grid.ToolTip = "Input includes cached tokens; the bar splits cached input from other input to avoid double counting.";
            AutomationProperties.SetName(grid, "Token breakdown, " + Count(totals?["input_tokens"]) + " input, including " + Count(totals?["cached_input_tokens"]) + " cached, and " + Count(totals?["output_tokens"]) + " output");
        });
        return panel;
    }

    private void UpdateFooterPresentation()
    {
        var state = EnginePresentation(backend.Snapshot); FooterDot.SetResourceReference(Shape.FillProperty, state.Tone);
        FooterDetails.Visibility = state.Tone == "Danger" ? Visibility.Visible : Visibility.Collapsed;
        if (!busy && !dirty && !editors.Values.Any(editor => editor.Dirty)) Footer.Text = state.Tone == "Success" && DateTimeOffset.TryParse(S(backend.Snapshot?["snapshot_at"]), out var updated) ? "Updated " + Math.Max(0, (int)(DateTimeOffset.UtcNow - updated).TotalSeconds) + "s ago · Connected to Discord" : state.Detail;
        UpdateTrayPresentation(state);
    }

    private void UpdateTrayPresentation(EngineVisual state)
    {
        var name = state.Tone switch { "Success" => "live", "Warning" => "paused", "Danger" => "error", _ => "neutral" };
        if (Appearance.TrayIsLight()) name += "-light";
        if (trayVariant == name) return;
        if (!trayVariants.TryGetValue(name, out var icon))
        {
            var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/tray-" + name + ".ico"));
            if (stream == null) return;
            using (stream.Stream) { using var source = new System.Drawing.Icon(stream.Stream); icon = (System.Drawing.Icon)source.Clone(); }
            trayVariants[name] = icon;
        }
        tray.Icon = icon; trayVariant = name;
    }

    private void ShowToast(string message)
    {
        if (webReady) return;
        var layer = (Grid)MainContent.Parent;
        if (toast != null) layer.Children.Remove(toast);
        toast = Surface(Text(message, 13)); toast.Margin = new Thickness(24, 0, 24, 48); toast.HorizontalAlignment = HorizontalAlignment.Right; toast.VerticalAlignment = VerticalAlignment.Bottom;
        AutomationProperties.SetLiveSetting(toast, AutomationLiveSetting.Polite); layer.Children.Add(toast);
        var current = toast;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) => { timer.Stop(); layer.Children.Remove(current); if (toast == current) toast = null; }; timer.Start();
    }

    private void AnimatePage()
    {
        if (!SystemParameters.ClientAreaAnimation || testMode) return;
        var time = TimeSpan.FromMilliseconds((double)FindResource("MotionMilliseconds"));
        PageBody.BeginAnimation(OpacityProperty, new DoubleAnimation(.5, 1, time));
        var transform = new TranslateTransform(); PageBody.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, time) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }
}
