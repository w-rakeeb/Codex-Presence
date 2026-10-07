using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexPresence;

public partial class MainWindow
{
    private IInputElement? focusBeforeDecision;

    private void NavigateFromKeyboard(string target)
    {
        Navigate(target);
        navigation[target].Focus();
    }

    public void ReportError(Exception error)
    {
        Footer.Text = UserFeedback.Describe(error);
        logs.Add(error.ToString());
        if (logs.Count > 150) logs.RemoveAt(0);
        if (diagnostics != null && page == "Tools") diagnostics.Text = string.Join(Environment.NewLine, logs);
    }

    private void UpdateActionAvailability()
    {
        foreach (var button in Elements<Button>(this).Where(button => button.Tag is string tag && tag == "AsyncAction")) button.IsEnabled = !busy;
        UpdateOverviewControls();
    }

    private void UpdateResponsiveLayout()
    {
        var narrow = ActualWidth < 460;
        PageScroll.Padding = new Thickness(narrow ? 12 : 20, 18, narrow ? 12 : 20, 16);
        SaveHost.Padding = new Thickness(narrow ? 12 : 20, 8, narrow ? 12 : 20, 8);
        StatusPanel.Margin = new Thickness(narrow ? 12 : 20, 4, narrow ? 12 : 20, 14);
        foreach (var button in navigation.Values)
        {
            var content = (StackPanel)button.Content;
            content.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
            ((FrameworkElement)content.Children[0]).Margin = narrow ? new Thickness(0, 0, 0, 4) : new Thickness(0, 0, 6, 0);
            button.FontSize = narrow ? 11.5 : 12.5;
            var label = (TextBlock)content.Children[1];
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.TextWrapping = TextWrapping.NoWrap;
            label.MaxWidth = narrow ? Math.Max(10, (ActualWidth - 34) / 5 - 8) : double.PositiveInfinity;
        }
        foreach (var sections in PageBody.Children.OfType<UniformGrid>()) sections.Columns = narrow ? 2 : 4;
        foreach (var row in Elements<Grid>(PageBody).Where(row => row.Tag is string tag && tag == "LayoutField"))
        {
            row.RowDefinitions.Clear();
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (narrow) row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var sizes = narrow ? new[] { new GridLength(1, GridUnitType.Star), new GridLength(38), new GridLength(38), new GridLength(0) } : new[] { new GridLength(1, GridUnitType.Star), new GridLength(104), new GridLength(35), new GridLength(35) };
            for (var index = 0; index < sizes.Length; index++) row.ColumnDefinitions[index].Width = sizes[index];
            for (var index = 0; index < row.Children.Count; index++)
            {
                var child = row.Children[index];
                Grid.SetRow(child, narrow && index > 0 ? 1 : 0);
                Grid.SetColumn(child, narrow && index > 0 ? index - 1 : index);
                Grid.SetColumnSpan(child, narrow && index == 0 ? 3 : 1);
            }
        }
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        AutomationProperties.SetName(MaximizeButton, WindowState == WindowState.Maximized ? "Restore window" : "Maximize window");
    }

    private void MaximizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateResponsiveLayout();
    }

    private void RenderStartupFailure(string message)
    {
        foreach (var button in navigation.Values) button.IsEnabled = false;
        PageBody.Children.Clear();
        Heading("Could not start", "Your saved settings have not been changed.");
        var card = Card("Startup issue");
        card.Children.Add(Text(message, 13));
        var retry = AsyncButton("Retry startup", InitializeContent);
        retry.Style = (Style)FindResource("PrimaryButton");
        retry.Margin = new Thickness(0, 12, 0, 8);
        card.Children.Add(retry);
        card.Children.Add(ActionButton("Open config folder", () => OpenPath(backend.Options.CodexHome)));
        SaveHost.Visibility = Visibility.Collapsed;
        ShowDashboard();
    }

    private async Task RunDiagnostic(string command, TextBox output)
    {
        Footer.Text = "Running " + command + "…";
        try { output.Text = await backend.Command(command); Footer.Text = "Diagnostics finished. Results are in Tools."; }
        catch (Exception error) { output.Text = error.Message; Footer.Text = "Diagnostics found an issue. See Tools for details."; }
    }

    private async Task ReloadDraft(bool confirm = true)
    {
        var reloadPage = page;
        var reloadVersion = editVersions.GetValueOrDefault(reloadPage);
        if (confirm && dirty && MessageBox.Show(this, "Discard unsaved changes on " + reloadPage + " and reload its saved settings?", "Reload settings", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Footer.Text = "Reloading " + reloadPage.ToLowerInvariant() + "…";
        await backend.LoadConfig();
        if (editVersions.GetValueOrDefault(reloadPage) != reloadVersion)
        {
            Footer.Text = "Saved settings loaded. Your newer edits are still unsaved.";
            return;
        }
        editors.Remove(reloadPage);
        if (reloadPage == "Settings") Appearance.Apply(this, backend.Options);
        if (page == reloadPage) { dirty = false; draft = null; draftBase = null; Navigate(page); }
        UpdateDraftMarkers();
        Footer.Text = reloadPage + " reloaded.";
    }

    private async Task ApplyPreferences(Preferences pending)
    {
        var previous = backend.Options;
        var preferenceRoot = testMode ? Path.Combine(root, "Data", "Verification", "Preferences") : root;
        var restart = backend.Running && !previous.SameMonitoring(pending);
        var saved = false;
        var startupChanged = false;
        try
        {
            await Task.Run(() => pending.Save(preferenceRoot));
            saved = true;
            if (!testMode) { await Task.Run(pending.ApplyStartup); startupChanged = true; }
            if (restart) await backend.Stop();
            backend.Options = pending;
            await backend.LoadConfig();
            Appearance.Apply(this, pending);
            ApplyDesktopPreferences();
            if (restart) await backend.Start();
        }
        catch (Exception error)
        {
            backend.Options = previous;
            try
            {
                if (saved) await Task.Run(() => previous.Save(preferenceRoot));
                if (startupChanged) await Task.Run(previous.ApplyStartup);
                await backend.LoadConfig();
                Appearance.Apply(this, previous);
                ApplyDesktopPreferences();
                if (restart && !backend.Running) await backend.Start();
            }
            catch (Exception recovery) { throw new IOException("Desktop settings could not be applied. Check Tools before restarting. " + error.Message + " Recovery: " + recovery.Message, error); }
            throw new IOException("Desktop settings could not be applied. Previous monitoring settings were restored. " + error.Message, error);
        }
    }

    private static string OriginalDocumentation(string appRoot)
    {
        var local = Path.Combine(appRoot, "Source", "docs", "index.md");
        return File.Exists(local) ? local : "https://github.com/w-rakeeb/Codex-Presence/blob/main/docs/index.md";
    }

    private void AttachFieldFeedback(TextBox box)
    {
        if (box.Parent is not Panel parent) return;
        var feedback = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 8) };
        feedback.SetResourceReference(TextBlock.ForegroundProperty, "Error");
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
        var index = parent.Children.IndexOf(box);
        parent.Children.Insert(index + 1, feedback);
        box.Tag = feedback;
        box.TextChanged += (_, _) => { feedback.Visibility = Visibility.Collapsed; box.SetResourceReference(BorderBrushProperty, "InputLine"); AutomationProperties.SetHelpText(box, ""); };
    }

    private void RejectField(TextBox box, string message, string? section = null)
    {
        if (section != null) ShowSettingsSection(section, false);
        if (box.Tag is TextBlock feedback) { feedback.Text = message; feedback.Visibility = Visibility.Visible; }
        box.SetResourceReference(BorderBrushProperty, "Error");
        AutomationProperties.SetHelpText(box, message);
        box.BringIntoView();
        box.Focus();
        throw new UserActionException(message);
    }

    private void ShowExitDecision()
    {
        ShowDashboard();
        if (DecisionPanel.Visibility == Visibility.Visible) return;
        focusBeforeDecision = Keyboard.FocusedElement;
        DecisionBody.Children.Clear();
        DecisionBody.Children.Add(Text("Unsaved changes", 20));
        var detail = Text("Review your changes before closing, or exit without saving them.", 13, Muted);
        detail.Margin = new Thickness(0, 8, 0, 16);
        DecisionBody.Children.Add(detail);
        var review = ActionButton("Review changes", () =>
        {
            var pending = dirty ? page : editors.First(pair => pair.Value.Dirty).Key;
            CloseDecision();
            Navigate(pending);
        });
        review.Style = (Style)FindResource("PrimaryButton");
        review.Margin = new Thickness(0, 0, 0, 8);
        DecisionBody.Children.Add(review);
        var discard = AsyncButton("Exit without saving", async () => { dirty = false; editors.Clear(); CloseDecision(); await ExitApp(); });
        discard.Margin = new Thickness(0, 0, 0, 8);
        DecisionBody.Children.Add(discard);
        DecisionBody.Children.Add(ActionButton("Cancel", CloseDecision));
        MainContent.IsEnabled = false;
        DecisionPanel.Visibility = Visibility.Visible;
        review.Focus();
    }

    private void CloseDecision()
    {
        DecisionPanel.Visibility = Visibility.Collapsed;
        MainContent.IsEnabled = true;
        if (focusBeforeDecision is UIElement { IsVisible: true, IsEnabled: true } previous) Keyboard.Focus(previous);
        else navigation[page].Focus();
        focusBeforeDecision = null;
    }
}
