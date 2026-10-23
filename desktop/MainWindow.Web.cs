using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CodexPresence;

public partial class MainWindow
{
    private WebView2? webInterface;
    private Task? webInitialization;
    private DispatcherTimer? webTimer;
    private bool webReady;
    private bool recoveringWeb;
    private string? lastWebPacket;
    private readonly ConditionalWeakTable<DependencyObject, WebKey> webKeys = new();
    private readonly Dictionary<string, FrameworkElement> webControls = new();
    private int nextWebKey;
    private sealed record WebKey(string Value);

    private Task EnsureWebInterface()
    {
        if (webInitialization?.IsCompleted == true && !webReady) webInitialization = null;
        return webInitialization ??= InitializeWebInterface();
    }

    private async Task InitializeWebInterface()
    {
        try
        {
            if (webInterface != null)
            {
                var previous = webInterface; webInterface = null;
                ShellRoot.Children.Remove(previous); previous.Dispose();
            }
            lastWebPacket = null;
            var folder = Path.Combine(root, "Web");
            if (!File.Exists(Path.Combine(folder, "index.html"))) throw new IOException("The bundled interface files are missing. Extract the complete release folder.");
            var canvas = ((SolidColorBrush)FindResource("Canvas")).Color;
            webInterface = new WebView2 { Margin = new Thickness(8, 40, 8, 8), Visibility = Visibility.Hidden, DefaultBackgroundColor = System.Drawing.Color.FromArgb(canvas.A, canvas.R, canvas.G, canvas.B) };
            Panel.SetZIndex(webInterface, 20);
            ShellRoot.Children.Add(webInterface);
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(root, "Data", testMode ? "Verification\\WebView" : "WebView"));
            await webInterface.EnsureCoreWebView2Async(environment);
            var core = webInterface.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = testMode;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = true;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.SetVirtualHostNameToFolderMapping("presence.local", folder, CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith("https://presence.local/", StringComparison.Ordinal)) e.Cancel = true; };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.WebMessageReceived += WebMessage;
            core.ProcessFailed += (sender, e) => { if (ReferenceEquals(sender, webInterface?.CoreWebView2)) HandleWebProcessFailure(e.ProcessFailedKind); };
            if (webTimer == null)
            {
                webTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                webTimer.Tick += (_, _) => { if (IsVisible && webReady && !windowResizing) SendWebState(); };
                Closed += (_, _) => { webTimer.Stop(); webInterface?.Dispose(); };
            }
            webTimer.Start();
            if (!testMode && windowPresentation.InterfaceRevision < 3)
            {
                windowPresentation.InterfaceRevision = 3;
                Width = Math.Min(1100, SystemParameters.WorkArea.Width);
                Height = Math.Min(860, SystemParameters.WorkArea.Height);
                Left = Math.Clamp(Left, SystemParameters.WorkArea.Left, Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - Width));
            }
            core.Navigate("https://presence.local/index.html");
            for (var attempt = 0; attempt < 100 && !webReady; attempt++) await Task.Delay(100);
            if (!webReady) throw new IOException("The interface did not finish loading. Reopen the dashboard to retry.");
        }
        catch (Exception error)
        {
            if (webInterface != null) webInterface.Visibility = Visibility.Hidden;
            SetNativeViewAccess(true);
            ReportError(new IOException("The new interface could not open. Install Microsoft Edge WebView2 Runtime, or restore the complete app folder. The native dashboard is available below.", error));
            if (args.Contains("--web-test")) throw;
        }
    }

    private void SetNativeViewAccess(bool enabled)
    {
        NativeViews.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        foreach (var native in new FrameworkElement[] { Navigation, PageScroll, SaveHost, StatusPanel })
        {
            System.Windows.Input.KeyboardNavigation.SetTabNavigation(native, enabled ? System.Windows.Input.KeyboardNavigationMode.Continue : System.Windows.Input.KeyboardNavigationMode.None);
            native.IsHitTestVisible = enabled;
        }
    }

    private void HandleWebProcessFailure(CoreWebView2ProcessFailedKind kind)
    {
        if (closing) return;
        logs.Add("Interface process event: " + kind);
        if (logs.Count > 150) logs.RemoveAt(0);
        if (kind is not (CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited)) return;
        webReady = false;
        if (webInterface != null) webInterface.Visibility = Visibility.Hidden;
        SetNativeViewAccess(true);
        Footer.Text = "Restoring the interface…";
        if (!recoveringWeb && !closing) _ = Dispatcher.InvokeAsync(RecoverWebInterface);
    }

    private async Task RecoverWebInterface()
    {
        if (recoveringWeb || closing) return;
        recoveringWeb = true;
        try
        {
            webTimer?.Stop();
            for (var attempt = 0; attempt < 2 && !closing; attempt++)
            {
                webInitialization = null;
                try { await EnsureWebInterface(); }
                catch (Exception error) { ReportError(error); }
                if (webReady) { Footer.Text = "Interface restored."; return; }
            }
            Footer.Text = "The interface could not reopen. Close and reopen the dashboard to retry; monitoring continues.";
        }
        finally { recoveringWeb = false; }
    }

    private async void WebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!ReferenceEquals(sender, webInterface?.CoreWebView2)) return;
        if (e.Source != "https://presence.local/index.html" || e.WebMessageAsJson.Length > 1048576) return;
        try
        {
            var message = JsonNode.Parse(e.WebMessageAsJson)!.AsObject();
            var type = S(message["type"], "");
            if (type == "ready")
            {
                webReady = true; webInterface!.Visibility = Visibility.Visible;
                SetNativeViewAccess(false);
                SendWebState(); return;
            }
            if (DecisionPanel.Visibility == Visibility.Visible && type is not "invoke" && !(type == "command" && S(message["name"]) == "cancel-decision")) return;
            if (type == "navigate" && navigation.ContainsKey(S(message["page"]))) Navigate(S(message["page"]));
            else if (type == "section" && new[] { "Appearance", "Activity", "Background", "Advanced" }.Contains(S(message["section"]))) ShowSettingsSection(S(message["section"]));
            else if (type == "command")
            {
                switch (S(message["name"]))
                {
                    case "engine-primary" when !backend.Running || !backend.Options.MonitorOnly: await Guard(!backend.Running ? StartEngine : TogglePresence); break;
                    case "stop": await Guard(StopEngine); break;
                    case "background": await Guard(RunInBackground); break;
                    case "exit": await Guard(ExitApp); break;
                    case "save" when page == "Manual": await Guard(SaveManualProfile); break;
                    case "reload" when page == "Manual": await Guard(ReloadManualProfile); break;
                    case "save" when SaveHost.Visibility == Visibility.Visible: await Guard(() => SaveDraft()); break;
                    case "reload" when SaveHost.Visibility == Visibility.Visible: await Guard(() => ReloadDraft()); break;
                    case "cancel-decision": CloseDecision(); break;
                    case "copy-tokens": Clipboard.SetText(Count(backend.Snapshot?["metrics"]?["totals"]?["total_tokens"])); Footer.Text = "Full token count copied."; break;
                    case "copy-stat":
                        var copied = S(message["field"], "") switch
                        {
                            "token" => Count(backend.Snapshot?["metrics"]?["totals"]?["total_tokens"]),
                            "cache" => Number(backend.Snapshot?["metrics"]?["totals"]?["cache_hit_ratio"]) is { } ratio ? (Math.Clamp(ratio, 0, 1) * 100).ToString("F1") + "%" : "—",
                            "context" => Number(ActiveContext(backend.Snapshot)) is { } used ? used.ToString("0.#") + "%" : "—",
                            "cost" => Cost(backend.Snapshot?["metrics"]?["totals"]?["known_cost_usd"], backend.Snapshot?["metrics"]?["totals"]?["pricing_status"]),
                            _ => ""
                        };
                        if (copied.Length > 0) { Clipboard.SetText(copied); Footer.Text = "Full value copied."; }
                        break;
                }
            }
            else if (webControls.TryGetValue(S(message["id"], ""), out var control) && control.IsEnabled)
            {
                if (type == "invoke" && control is Button button) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else if (type == "expand" && control is Expander expander) expander.IsExpanded = B(message["value"]);
                else if (type == "edit")
                {
                    if (control is CheckBox check) check.IsChecked = B(message["value"]);
                    else if (control is ComboBox choice && message["value"] is JsonValue indexValue && indexValue.TryGetValue<int>(out var index) && index >= 0 && index < choice.Items.Count) choice.SelectedIndex = index;
                    else if (control is TextBox box && !box.IsReadOnly)
                    {
                        var text = S(message["value"], "");
                        box.Text = box.MaxLength > 0 && text.Length > box.MaxLength ? text[..box.MaxLength] : text;
                    }
                }
            }
            SendWebState();
        }
        catch (Exception error) { ReportError(error); SendWebState(); }
    }

    private void SendWebState()
    {
        if (!webReady || webInterface?.CoreWebView2 == null) return;
        var packet = BuildWebState().ToJsonString();
        if (packet == lastWebPacket) return;
        lastWebPacket = packet;
        webInterface.CoreWebView2.PostWebMessageAsJson(packet);
    }

    private JsonObject BuildWebState()
    {
        webControls.Clear();
        var nodes = new JsonArray();
        foreach (var child in PageBody.Children.OfType<FrameworkElement>().Skip(2))
        {
            if (child is UniformGrid { Tag: Action<string> }) continue;
            if (child.Tag?.ToString() == "DiscordPreview") continue;
            if (page == "Overview")
            {
                if (child is not Border { Child: StackPanel content } || content.Children.OfType<TextBlock>().FirstOrDefault()?.Text is not ("Active sessions" or "Limits & credits" or "Metrics")) continue;
            }
            if (WebNode(child, child is Border) is { } node) nodes.Add(node);
        }
        if (page == "Overview" && Elements<Expander>(PageBody).FirstOrDefault(expander => ContentText(expander.Header) == "Plan and speed details") is { } planDetails && WebNode(planDetails) is { } planNode) nodes.Insert(0, planNode);
        var visual = EnginePresentation(backend.Snapshot);
        var theme = new JsonObject();
        foreach (var pair in new[] { ("--color-base-100", "Surface"), ("--color-base-200", "Canvas"), ("--color-base-300", "Control"), ("--color-base-content", "Text"), ("--color-primary", "Accent"), ("--color-primary-content", "OnAccent"), ("--color-secondary", "Secondary"), ("--color-secondary-content", "Canvas"), ("--color-accent", "Accent"), ("--color-accent-content", "OnAccent"), ("--color-neutral", "Control"), ("--color-neutral-content", "Text"), ("--color-info", "DataInput"), ("--color-info-content", "OnAccent"), ("--color-success", "Success"), ("--color-success-content", "Canvas"), ("--color-warning", "Warning"), ("--color-warning-content", "Canvas"), ("--color-error", "Danger"), ("--color-error-content", "Canvas"), ("--divider", "Line"), ("--muted", "Muted"), ("--secondary", "Secondary"), ("--input-border", "InputLine"), ("--discord", "DiscordCard") })
        {
            var color = ((SolidColorBrush)FindResource(pair.Item2)).Color;
            theme[pair.Item1] = $"rgba({color.R},{color.G},{color.B},{(color.A / 255d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})";
        }
        theme["--radius-box"] = "12px"; theme["--radius-field"] = "8px"; theme["--border"] = "1px"; theme["--depth"] = "0";
        var decision = DecisionPanel.Visibility == Visibility.Visible ? new JsonArray(DecisionBody.Children.OfType<FrameworkElement>().Select(element => (JsonNode?)WebNode(element)).ToArray()) : null;
        return new JsonObject
        {
            ["page"] = page, ["section"] = settingsSection, ["nodes"] = nodes, ["running"] = backend.Running, ["busy"] = busy,
            ["publicationEnabled"] = backend.PublicationEnabled, ["monitorOnly"] = backend.Options.MonitorOnly, ["dirty"] = page == "Manual" ? manualProfileUnsaved : dirty, ["canSave"] = page == "Manual" || SaveHost.Visibility == Visibility.Visible,
            ["dirtyTabs"] = new JsonArray(navigation.Keys.Where(name => name == "Manual" ? manualProfileUnsaved : name == page ? dirty : editors.TryGetValue(name, out var retained) && retained.Dirty).Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
            ["footer"] = Footer.Text, ["visual"] = new JsonObject { ["headline"] = visual.Headline, ["detail"] = visual.Detail, ["tone"] = visual.Tone },
            ["snapshot"] = backend.Snapshot?.DeepClone(), ["decision"] = decision, ["theme"] = theme,
            ["previewVisible"] = backend.Running && backend.Snapshot?["preview"] != null && !B(backend.Snapshot?["startup_waiting"]) && (B(backend.Snapshot?["manual_mode"]) || backend.Snapshot?["sessions"] is JsonArray { Count: > 0 }) && visualStateOverride is not ("stopped" or "loading" or "idle"),
            ["style"] = Elements<ComboBox>(PageBody).FirstOrDefault(combo => AutomationProperties.GetName(combo) == "Interface style")?.SelectedValue?.ToString() ?? backend.Options.InterfaceStyle,
            ["light"] = ((SolidColorBrush)FindResource("Canvas")).Color.R > 128
        };
    }

    private JsonObject? WebNode(FrameworkElement element, bool card = false)
    {
        if (element.Visibility != Visibility.Visible) return null;
        var id = webKeys.GetValue(element, _ => new WebKey((++nextWebKey).ToString(System.Globalization.CultureInfo.InvariantCulture))).Value;
        var node = new JsonObject { ["id"] = id, ["disabled"] = !element.IsEnabled };
        webControls[id] = element;
        JsonArray Children(IEnumerable<FrameworkElement> children) => new(children.Select(child => (JsonNode?)WebNode(child)).Where(child => child != null).ToArray());
        switch (element)
        {
            case TextBlock text:
                node["kind"] = "text"; node["text"] = text.Text;
                node["tone"] = text.Foreground is SolidColorBrush brush && brush.Color == ((SolidColorBrush)FindResource("Muted")).Color ? "muted" : "normal";
                node["heading"] = text.FontWeight.ToOpenTypeWeight() >= 600 && text.FontSize >= 14;
                break;
            case CheckBox check:
                node["kind"] = "toggle"; node["value"] = check.IsChecked == true; node["label"] = AutomationProperties.GetName(check) is { Length: > 0 } name ? name : ContentText(check.Content);
                node["children"] = check.Content is FrameworkElement checkContent ? new JsonArray(WebNode(checkContent)) : new JsonArray();
                break;
            case ComboBox combo:
                node["kind"] = "select"; node["label"] = AutomationProperties.GetName(combo); node["value"] = combo.SelectedIndex;
                node["options"] = new JsonArray(combo.Items.Cast<object>().Select(item => (JsonNode?)JsonValue.Create(item is Choice choice ? choice.Title : item.ToString())).ToArray());
                break;
            case TextBox box:
                node["kind"] = "input"; node["label"] = AutomationProperties.GetName(box); node["value"] = box.Text; node["maximum"] = box.MaxLength;
                node["multiline"] = box.AcceptsReturn; node["readOnly"] = box.IsReadOnly;
                node["error"] = box.Tag is TextBlock { Visibility: Visibility.Visible } feedback ? feedback.Text : "";
                break;
            case Button button:
                node["kind"] = "button"; node["label"] = AutomationProperties.GetName(button); node["text"] = ContentText(button.Content); node["tip"] = button.ToolTip?.ToString();
                node["primary"] = button.Style == FindResource("PrimaryButton"); break;
            case Expander expander:
                node["kind"] = "collapse"; node["label"] = ContentText(expander.Header); node["expanded"] = expander.IsExpanded;
                node["children"] = expander.Content is FrameworkElement expanded ? new JsonArray(WebNode(expanded)) : new JsonArray(); break;
            case ProgressBar progress:
                node["kind"] = "progress"; node["value"] = progress.Value; node["maximum"] = progress.Maximum; node["label"] = AutomationProperties.GetName(progress); break;
            case Border border when card && border.Child is StackPanel body && body.Children.OfType<TextBlock>().FirstOrDefault() is { } title:
                node["kind"] = "card"; node["title"] = title.Text; node["children"] = Children(body.Children.OfType<FrameworkElement>().Where(child => !ReferenceEquals(child, title))); break;
            case Border border:
                node["kind"] = "stack"; node["children"] = border.Child is FrameworkElement content ? new JsonArray(WebNode(content)) : new JsonArray(); break;
            case Panel panel:
                node["kind"] = panel is Grid grid && grid.ColumnDefinitions.Count > 1 ? "row" : panel is WrapPanel || panel is StackPanel { Orientation: Orientation.Horizontal } ? "actions" : "stack";
                node["layoutField"] = panel.Tag?.ToString() == "LayoutField";
                node["children"] = Children(panel.Children.OfType<FrameworkElement>()); break;
            default: return null;
        }
        return node;
    }

    private static string ContentText(object? content) => content switch
    {
        string text => text,
        TextBlock text => text.Text,
        Panel panel => string.Join(" ", panel.Children.OfType<FrameworkElement>().Select(ContentText).Where(text => text.Length > 0)),
        _ => ""
    };
}
