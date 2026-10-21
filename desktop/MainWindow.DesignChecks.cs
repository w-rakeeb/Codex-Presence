using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexPresence;

public partial class MainWindow
{
    private async Task DesignChecks(List<string> checks, string evidence)
    {
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("Design verification failed: " + label); checks.Add(label); }
        var folder = Path.Combine(evidence, "Design"); Directory.CreateDirectory(folder);
        var configBefore = File.ReadAllText(backend.ConfigPath);
        var preferencesBefore = JsonSerializer.Serialize(backend.Options);
        var sample = backend.Snapshot!.DeepClone().AsObject();
        var originalPreview = sample["preview"]!.ToJsonString();
        var oldWidth = Width; var oldHeight = Height; var oldBusy = busy;
        var contrast = new List<object>();
        void Capture(string name, double scale = 1)
        {
            UpdateLayout();
            var image = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth * scale), (int)Math.Ceiling(ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            image.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(folder, name + ".png")); encoder.Save(file);
        }
        double Luminance(Color color)
        {
            double Linear(byte part) { var value = part / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        double Contrast(string foreground, string background)
        {
            var a = Luminance(((SolidColorBrush)FindResource(foreground)).Color); var b = Luminance(((SolidColorBrush)FindResource(background)).Color);
            return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
        }
        void Fresh(string target)
        {
            dirty = false; editors.Clear(); draft = null; draftBase = null; if (target == "Settings") settingsSection = "Appearance"; Navigate(target);
            if (target == "Overview") RenderOverview(sample);
            UpdateLayout(); UpdateFooterPresentation();
        }
        try
        {
            busy = false; Width = 640; Height = 820; SetValue(Appearance.ThemeModeProperty, "inherit");
            Appearance.Apply(this, new Preferences());
            visualStateOverride = "live";
            foreach (var target in navigation.Keys) { Fresh(target); Capture(target); }
            Fresh("Layout"); var pinned = PinnedPreviewHost.TransformToAncestor(this).Transform(new Point()); PageScroll.ScrollToVerticalOffset(300); UpdateLayout();
            Check(PinnedPreviewHost.IsVisible && PinnedPreviewHost.TransformToAncestor(this).Transform(new Point()) == pinned, "Layout preview remains pinned while field controls scroll");
            Check(PinnedPreviewHost.Child is Border, "Layout uses the shared Discord preview card");
            foreach (var state in new[] { "live", "paused", "stopped", "error", "waiting", "loading", "idle" })
            {
                visualStateOverride = state; Fresh("Overview"); UpdateFooterPresentation(); Capture("State-" + state);
                Check(EnginePresentation(sample).Headline.Length > 0 && EnginePresentation(sample).Detail.Length > 0, state + " has a named headline and recovery or status detail");
            }
            visualStateOverride = "live";
            foreach (var mode in new[] { "dark", "light" })
                foreach (var theme in new[] { "neutral", "slate", "forest", "dusk" })
                {
                    Appearance.Apply(this, new Preferences { ColorMode = mode, ColorTheme = theme });
                    foreach (var foreground in new[] { "Text", "Secondary", "Muted" })
                        foreach (var background in new[] { "Canvas", "Surface", "Control", "Hover", "DiscordCard" })
                        {
                            var ratio = Contrast(foreground, background); contrast.Add(new { mode, theme, foreground, background, ratio });
                            Check(ratio >= 4.5, mode + " / " + theme + " " + foreground + " on " + background + " meets 4.5:1");
                        }
                    foreach (var foreground in new[] { "Accent", "AccentHover", "AccentPressed" }) Check(Contrast("OnAccent", foreground) >= 4.5, mode + " / " + theme + " primary " + foreground + " meets 4.5:1");
                    Check(Contrast("InputLine", "Control") >= 3 && Contrast("InputLine", "Canvas") >= 3, mode + " / " + theme + " input outlines meet 3:1");
                    Fresh("Overview"); Capture("Overview-" + mode + "-" + theme);
                }
            Appearance.Apply(this, new Preferences());
            foreach (var size in new[] { (560, 640), (1280, 960) })
            {
                Width = size.Item1; Height = size.Item2;
                foreach (var target in navigation.Keys)
                {
                    Fresh(target);
                    Check(PageBody.ActualWidth <= PageScroll.ActualWidth && (size.Item1 <= 720 || PageBody.ActualWidth > 720) && navigation.Values.All(button => button.ActualWidth >= 32), target + " fits " + size.Item1 + " × " + size.Item2 + " with content sized to the available window width");
                    Capture(target + "-" + size.Item1);
                }
            }
            Width = 640; Height = 820;
            foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
                foreach (var target in navigation.Keys)
                {
                    Fresh(target); Capture(target + "-render-scale-" + (scale * 100).ToString("0"), scale);
                    Check(PageScroll.ViewportHeight > 70 && Footer.FontSize >= 12, target + " renders at " + (scale * 100) + "% without losing scrolling or footer");
                }
            Appearance.Apply(this, new Preferences(), true);
            foreach (var target in navigation.Keys) { Fresh(target); Capture(target + "-high-contrast"); }
            Check(ReferenceEquals(FindResource("Text"), SystemColors.WindowTextBrush) && ReferenceEquals(FindResource("Accent"), SystemColors.HighlightBrush), "High contrast uses Windows text, canvas and highlight brushes");
            Check(ReferenceEquals(FindResource("SelectedText"), SystemColors.HighlightTextBrush), "Selected controls use the Windows high-contrast highlight text color");
            Appearance.Apply(this, new Preferences()); Fresh("Overview");
            var preview = Elements<Border>(PageBody).Single(border => border.Tag?.ToString() == "DiscordPreview");
            var text = Elements<TextBlock>(preview).Select(block => block.Text).ToArray();
            Check(new[] { "app_name", "details", "state" }.All(key => text.Contains(S(sample["preview"]?[key], ""))), "Preview renders every engine text line verbatim without changing separators or labels");
            Check(sample["preview"]!.ToJsonString() == originalPreview && File.ReadAllText(backend.ConfigPath) == configBefore && JsonSerializer.Serialize(backend.Options) == preferencesBefore, "Visual scenes preserve preview data, engine settings and desktop preferences exactly");
            Check(Elements<TextBlock>(PageBody).Where(block => block.IsVisible).All(block => block.FontSize >= 12), "Every visible dashboard text block is at least 12px");
            Check(navigation.Values.All(button => button.Style == FindResource("TabButton")), "All five navigation buttons share the segmented tab style");
            var vertical = (System.Windows.Controls.Primitives.ScrollBar)PageScroll.Template.FindName("PART_VerticalScrollBar", PageScroll);
            Check(vertical.ActualWidth <= 8, "The content scrollbar keeps an eight-pixel interaction track");
            Fresh("Settings");
            var themeChoice = Elements<ComboBox>(PageBody).Single(combo => AutomationProperties.GetName(combo) == "Color mode");
            themeChoice.SelectedValue = "system";
            Check((string)GetValue(Appearance.ThemeModeProperty) == "system" && backend.Options.ColorMode is "dark" or "light" && JsonSerializer.Serialize(backend.Options) == preferencesBefore && File.ReadAllText(backend.ConfigPath) == configBefore, "System theme is a presentation override without changing the saved settings format");
            SetPresentationTheme("inherit"); Appearance.Apply(this, backend.Options);
            var uiFixture = Path.Combine(folder, "WindowStateFixture");
            var savedState = new WindowPresentationState { Width = 800, Height = 900, Left = 80, Top = 120, LastTab = "Tools", ThemeMode = "system" };
            await savedState.Save(uiFixture);
            var restoredState = WindowPresentationState.Load(uiFixture);
            Check(restoredState.Width == 800 && restoredState.Height == 900 && restoredState.Left == 80 && restoredState.Top == 120 && restoredState.LastTab == "Tools" && restoredState.ThemeMode == "system", "Window size, position, last tab and system theme round-trip separately from engine settings");
            NavigateFromKeyboard("Overview"); var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, Key.Right) { RoutedEvent = Keyboard.PreviewKeyDownEvent }; navigation["Overview"].RaiseEvent(key);
            Check(page == "Privacy" && navigation["Privacy"].IsKeyboardFocused, "Right arrow moves keyboard focus and selection between tabs");
            Fresh("Overview"); visualStateOverride = null;
            File.WriteAllText(Path.Combine(folder, "contrast.json"), JsonSerializer.Serialize(contrast, Backend.JsonOptions));
            File.WriteAllText(Path.Combine(folder, "verification-scope.txt"), "All screenshots use isolated monitoring-only session fixtures. Render-scale images use WPF rasterization at 96, 120, 144 and 192 DPI; actual Windows display scaling and mixed-monitor transitions were not changed or tested. High-contrast resources are exercised without changing the user’s OS settings. Discord payload text, settings format and save behavior remain unchanged.");
        }
        finally
        {
            visualStateOverride = null; busy = oldBusy; Width = oldWidth; Height = oldHeight;
            Appearance.Apply(this, backend.Options);
        }
    }
}
