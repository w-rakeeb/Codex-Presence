using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private void RenderDiscordApplication()
    {
        var application = draft!["discord_application"]!.AsObject();
        var card = SettingsCard("Activity", "DISCORD APPLICATION", "Use the built-in application or connect your own. An Application ID is public; no bot token or permissions are needed.");
        var source = EditChoice(card, application, "mode", "Application source", new[] { "default", "custom" });
        var id = EditText(card, application, "application_id", "Custom Application ID", true, 32);
        AttachFieldFeedback(id);
        var images = new StackPanel();
        images.Children.Add(Text("Upload images to your application's Rich Presence → Art Assets. Enter their asset keys below; leave a key blank to omit its image.", 13, Muted));
        var large = EditText(images, application, "large_image_key", "Custom large image key", maximum: 256);
        var small = EditText(images, application, "small_image_key", "Custom small image key", true, 256);
        var artwork = new Expander { Header = "Custom application images", Content = images, Margin = new Thickness(0, 8, 0, 8) };
        card.Children.Add(artwork);
        var defaults = new StackPanel();
        Row(defaults, "Codex App", S(draft["discord_client_id_desktop"]));
        Row(defaults, "CLI / VS Code / ChatGPT", S(draft["discord_client_id"]));
        card.Children.Add(new Expander { Header = "Built-in application IDs", Content = defaults, Margin = new Thickness(0, 4, 0, 8) });
        card.Children.Add(ActionButton("Developer portal", () => Process.Start(new ProcessStartInfo("https://discord.com/developers/applications") { UseShellExecute = true })));
        void UpdateControls()
        {
            var custom = source.SelectedValue?.ToString() == "custom";
            if (!custom && !string.IsNullOrWhiteSpace(id.Text) && !ValidDiscordApplicationId(id.Text.Trim())) id.Text = "";
            id.IsEnabled = custom; large.IsEnabled = custom; small.IsEnabled = custom; artwork.IsEnabled = custom;
            if (!custom && id.Tag is TextBlock feedback) { feedback.Visibility = Visibility.Collapsed; id.SetResourceReference(BorderBrushProperty, "InputLine"); AutomationProperties.SetHelpText(id, ""); }
        }
        source.SelectionChanged += (_, _) => UpdateControls();
        UpdateControls();
        collectValues.Add(() =>
        {
            var value = id.Text.Trim();
            if ((source.SelectedValue?.ToString() == "custom" || value.Length > 0) && !ValidDiscordApplicationId(value))
                RejectField(id, "Enter the 17–20 digit Application ID from Discord's General Information page. Do not enter a bot token.", "Activity");
            application["application_id"] = string.IsNullOrWhiteSpace(value) ? null : JsonValue.Create(value);
        });
    }

    private static bool ValidDiscordApplicationId(string value) => value.Length is >= 17 and <= 20 && value[0] != '0' && value.All(character => character is >= '0' and <= '9') && ulong.TryParse(value, out var id) && id > 0;

    private async Task DiscordApplicationChecks(List<string> checks)
    {
        void Check(bool result, string label) { if (!result) throw new InvalidOperationException("Verification failed: " + label); checks.Add(label); }
        T Named<T>(string name) where T : DependencyObject
        {
            Elements<Expander>(PageBody).Single(item => item.Header?.ToString() == "Custom application images").IsExpanded = true;
            UpdateLayout();
            return Elements<T>(PageBody).Single(item => AutomationProperties.GetName(item) == name);
        }
        void Fresh() { dirty = false; editors.Clear(); draft = null; draftBase = null; Navigate("Settings"); ShowSettingsSection("Activity"); UpdateLayout(); }
        Fresh();
        var source = Named<ComboBox>("Application source");
        var id = Named<TextBox>("Custom Application ID");
        Check(source.SelectedValue?.ToString() == "default" && !id.IsEnabled && !Named<TextBox>("Custom large image key").IsEnabled, "Default application retains built-in IDs and disables custom fields");
        source.SelectedValue = "custom";
        Check(id.IsEnabled && Named<TextBox>("Custom small image key").IsEnabled, "Selecting Custom enables the ID and application-specific image fields");
        var engineId = backend.ProcessId;
        foreach (var value in new[] { "", "token.value.secret", "1478395304624652", "0000000000000000000", "18446744073709551616", "１２３４５６７８９０１２３４５６７８９", "https://discord.com/123456789012345678" })
        {
            id.Text = value;
            var before = File.ReadAllText(backend.ConfigPath);
            var rejected = false;
            try { await SaveDraft(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && File.ReadAllText(backend.ConfigPath) == before && id.IsKeyboardFocused && id.Tag is TextBlock feedback && feedback.Visibility == Visibility.Visible && backend.ProcessId == engineId, "Invalid custom ID is focused and rejected without replacing settings or restarting: " + (value.Length == 0 ? "empty" : value));
        }
        source.SelectedValue = "default";
        await SaveDraft();
        Check(backend.Config?["discord_application"]?["application_id"] == null, "Returning to Default discards an invalid ID draft instead of storing token-like input");
        source = Named<ComboBox>("Application source"); source.SelectedValue = "custom";
        id = Named<TextBox>("Custom Application ID");
        id.Text = " 123456789012345678 ";
        Named<TextBox>("Custom large image key").Text = " own-large ";
        Named<TextBox>("Custom small image key").Text = "own-small";
        await SaveDraft();
        Check(S(backend.Config?["discord_application"]?["application_id"]) == "123456789012345678" && S(backend.Config?["discord_application"]?["large_image_key"]) == "own-large", "Custom ID and artwork keys are validated, trimmed and saved");
        for (var attempt = 0; attempt < 12 && S(backend.Snapshot?["discord_application_id"]) != "123456789012345678"; attempt++) await Task.Delay(250);
        Check(S(backend.Snapshot?["discord_application_id"]) == "123456789012345678" && backend.ProcessId == engineId && !B(backend.Snapshot?["presence_enabled"]), "The existing monitoring engine reloads a custom ID without publishing fixture activity or restarting");
        Check(S(backend.Snapshot?["preview"]?["large_image_key"]) == "own-large" && S(backend.Snapshot?["preview"]?["small_image_key"]) == "own-small", "Custom preview uses assets from the selected application");
        Named<TextBox>("Custom small image key").Text = "";
        await SaveDraft();
        Check(backend.Config?["discord_application"]?["small_image_key"] == null, "A blank custom small-image key omits the image");
        Named<ComboBox>("Application source").SelectedValue = "default";
        await SaveDraft();
        for (var attempt = 0; attempt < 12 && S(backend.Snapshot?["discord_application_id"]) == "123456789012345678"; attempt++) await Task.Delay(250);
        var expected = S(backend.Config?["display"]?["desktop_presence_design"]) == "chat_gpt_app" ? S(backend.Config?["discord_client_id"]) : S(backend.Config?["discord_client_id_desktop"]);
        Check(S(backend.Snapshot?["discord_application_id"]) == expected && backend.ProcessId == engineId, "Default restores the built-in application on the same engine");
        Check(S(backend.Config?["discord_application"]?["application_id"]) == "123456789012345678" && S(backend.Config?["discord_application"]?["large_image_key"]) == "own-large", "Returning to Default retains the custom ID and image keys for reuse");
        Named<ComboBox>("Application source").SelectedValue = "custom";
        await ReloadDraft(false); UpdateLayout();
        Check(Named<ComboBox>("Application source").SelectedValue?.ToString() == "default" && !Named<TextBox>("Custom Application ID").IsEnabled, "Reload restores the saved source and dependent controls");
        Fresh();
        Check(Named<TextBox>("Custom Application ID").Text == "123456789012345678", "The saved custom ID survives rebuilding the Settings view");
    }
}
