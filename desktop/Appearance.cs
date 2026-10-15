using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace CodexPresence;

public static class Appearance
{
    public static readonly DependencyProperty ThemeModeProperty = DependencyProperty.RegisterAttached("ThemeMode", typeof(string), typeof(Appearance), new PropertyMetadata("inherit"));
    public static void Apply(Window window, Preferences preferences, bool? highContrast = null)
    {
        var system = (string)window.GetValue(ThemeModeProperty) == "system";
        var light = system ? SystemIsLight() : preferences.ColorMode == "light";
        var theme = preferences.ColorTheme is "slate" or "forest" or "dusk" ? preferences.ColorTheme : "neutral";
        var palette = (ResourceDictionary)window.FindResource((light ? "Light" : "Dark") + theme);
        foreach (DictionaryEntry entry in palette) window.Resources[entry.Key] = entry.Value;
        window.Resources["MotionMilliseconds"] = SystemParameters.ClientAreaAnimation ? 150d : 0d;
        window.Resources["CardRadius"] = new CornerRadius(preferences.InterfaceStyle switch { "soft" => 14, "chatgpt" => 12, "paper" => 4, "studio" => 8, _ => 10 });
        window.Resources["ControlRadius"] = new CornerRadius(preferences.InterfaceStyle == "soft" ? 8 : 6);
        window.Resources["InputRadius"] = new CornerRadius(preferences.InterfaceStyle == "soft" ? 8 : 6);
        window.Resources["CardPadding"] = new Thickness(preferences.InterfaceStyle == "soft" ? 20 : 16);
        window.Resources["HighContrastActive"] = highContrast ?? SystemParameters.HighContrast;
        if (highContrast ?? SystemParameters.HighContrast)
        {
            foreach (var key in new[] { "Canvas", "Surface", "Control", "DiscordCard", "Hover", "Pressed", "SuccessBg", "WarningBg", "DangerBg" }) window.Resources[key] = SystemColors.WindowBrush;
            foreach (var key in new[] { "Text", "Secondary", "Muted", "Disabled", "Neutral", "Success", "Warning", "Danger", "Error", "InputLine", "Line", "Scrollbar" }) window.Resources[key] = SystemColors.WindowTextBrush;
            foreach (var key in new[] { "Accent", "AccentHover", "AccentPressed", "Selected", "Selection", "FocusRing", "DataInput", "DataCached", "DataOutput" }) window.Resources[key] = SystemColors.HighlightBrush;
            window.Resources["OnAccent"] = SystemColors.HighlightTextBrush;
            window.Resources["SelectedText"] = SystemColors.HighlightTextBrush;
        }
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var dark = light ? 0 : 1;
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            var corner = 2;
            DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int));
            var backdrop = (highContrast ?? SystemParameters.HighContrast) ? 1 : 2;
            DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int));
        }
    }
    private static bool SystemIsLight()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); return key?.GetValue("AppsUseLightTheme") is int value && value != 0; }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }
    public static bool TrayIsLight()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); return key?.GetValue("SystemUsesLightTheme") is int value && value != 0; }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
