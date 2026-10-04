using System;
using System.Windows;
using System.Windows.Media;

namespace CodexPresence;

public static class Appearance
{
    public static void Apply(Window window, Preferences preferences)
    {
        if (preferences.InterfaceStyle is not ("minimal" or "soft" or "chatgpt" or "paper" or "studio")) preferences.InterfaceStyle = "minimal";
        if (preferences.ColorMode is not ("dark" or "light")) preferences.ColorMode = "dark";
        var light = preferences.ColorMode == "light";
        var colors = new (string Key, string Dark, string Light)[]
        {
            ("Canvas", "#090909", "#FAFAFA"), ("Surface", "#111111", "#FFFFFF"),
            ("Control", "#181818", "#F4F4F4"), ("Hover", "#2A2A2A", "#E8E8E8"),
            ("Line", "#303030", "#D7D7D7"), ("Text", "#FAFAFA", "#171717"),
            ("Muted", "#BBBBBB", "#5A5A5A"), ("Accent", "#FFFFFF", "#171717"),
            ("OnAccent", "#090909", "#FFFFFF"), ("AccentHover", "#DCDCDC", "#353535"),
            ("AccentPressed", "#BFBFBF", "#4A4A4A"), ("Selected", "#262626", "#E8E8E8"),
            ("Selection", "#4A4A4A", "#CBCBCB"), ("Scrollbar", "#505050", "#A0A0A0")
        };
        foreach (var color in colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? color.Light : color.Dark));
            brush.Freeze();
            window.Resources[color.Key] = brush;
        }
        var soft = preferences.InterfaceStyle == "soft";
        var chat = preferences.InterfaceStyle == "chatgpt";
        var paper = preferences.InterfaceStyle == "paper";
        var studio = preferences.InterfaceStyle == "studio";
        window.Resources["BodyFont"] = new FontFamily(soft || paper ? "Segoe UI, Arial" : "Segoe UI Variable Text, Segoe UI");
        window.Resources["HeadingFont"] = new FontFamily(soft ? "Georgia, Cambria" : paper ? "Cambria, Georgia" : "Segoe UI Variable Display, Segoe UI");
        window.Resources["HeadingWeight"] = soft || paper ? FontWeights.Normal : FontWeights.SemiBold;
        window.Resources["CardTitleSize"] = soft || paper ? 17d : studio ? 15d : 14d;
        window.Resources["ControlRadius"] = new CornerRadius(soft ? 12 : chat ? 18 : studio ? 8 : 6);
        window.Resources["CardRadius"] = new CornerRadius(soft ? 18 : chat ? 12 : studio ? 14 : paper ? 0 : 6);
        window.Resources["InputRadius"] = new CornerRadius(soft ? 10 : chat ? 12 : studio ? 8 : 5);
        window.Resources["CardPadding"] = new Thickness(soft || paper ? 20 : chat || studio ? 18 : 16);
        window.Resources["CardSpacing"] = new Thickness(0, 0, 0, soft || paper ? 16 : 12);
        window.Resources["HeadingSize"] = paper ? 27d : soft ? 25d : chat ? 24d : studio ? 23d : 22d;
        window.Resources["CardBorder"] = paper ? new Thickness(0, 1, 0, 1) : new Thickness(chat ? 0 : 1);
        window.Resources["BrandIcon"] = Geometry.Parse(paper ? "M4,3 L18,3 L21,6 L21,22 L4,22 Z M18,3 L18,7 L21,7 M8,11 L17,11 M8,15 L17,15"
            : studio ? "M3,3 L10,3 L10,10 L3,10 Z M14,3 L21,3 L21,10 L14,10 Z M3,14 L10,14 L10,21 L3,21 Z M14,14 L21,14 L21,21 L14,21 Z"
            : soft
            ? "M12,2 L14,8 L20,6 L16,12 L22,14 L16,16 L18,22 L12,18 L8,22 L8,16 L2,14 L8,10 L6,4 L12,8 Z"
            : chat ? "M5,3 L19,3 Q22,3 22,6 L22,16 Q22,19 19,19 L10,19 L4,23 L4,18 Q2,18 2,15 L2,6 Q2,3 5,3 Z M7,8 L17,8 M7,13 L14,13"
            : "M11,3 L3,12 L11,21 M15,3 L23,12 L15,21");
    }
}
