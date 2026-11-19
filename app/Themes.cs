using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace D4;

public sealed record Theme(
    string Id, string Name, bool Dark,
    string Bg, string Card, string Text, string Muted, string Accent, string Accent2,
    string S1, string S2, string S3, string M1, string M2, string M3, string Sun, double Stars);

public static class ThemeManager
{
    public static readonly Theme[] All =
    {
        new("ocean",  "Океан",   true,  "#0C1228", "#CC141B33", "#EEF2FB", "#A9B4CC", "#7FB0FF", "#A68BFF",
            "#0F1B3D", "#3A4FA8", "#8FB4FF", "#26356B", "#1B2750", "#121A38", "#CFE0FF", 1.0),
        new("sunset", "Закат",   true,  "#1E0F1F", "#CC2A1526", "#FFF1EC", "#D1AEB4", "#FF9A6B", "#FF5F8F",
            "#3A1C3F", "#D9566F", "#FFB36B", "#7A2C4E", "#4F1D3D", "#2C1230", "#FFE1A8", 0.35),
        new("forest", "Лес",     true,  "#08191A", "#CC0F2A24", "#EAFBF2", "#9FC4B3", "#6FE0A5", "#C2EE72",
            "#0E2A22", "#2F7A5C", "#B8E6B0", "#1F5A45", "#14402F", "#0B2A1F", "#F3FFD0", 0.5),
        new("neon",   "Неон",    true,  "#0F0822", "#CC1B1038", "#F5EEFF", "#B7A6D9", "#B894FF", "#FF7AD9",
            "#150C2E", "#6A2FB5", "#FF7AD9", "#3B1D78", "#2A1457", "#1A0B3A", "#FFD1F3", 1.0),
        new("light",  "Светлая", false, "#EAF1FC", "#CCFFFFFF", "#16203A", "#55627D", "#2F6BFF", "#7A4DFF",
            "#7FB0FF", "#BCD7FF", "#F4F8FF", "#6F94D6", "#5578B8", "#3D5C96", "#FFF6C9", 0.0),
    };

    public static Theme Current { get; private set; } = All[0];

    /// <summary>Срабатывает после смены темы.</summary>
    public static event Action? Changed;

    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    // Кисти создаются один раз и дальше только плавно меняют цвет — все окна перекрашиваются на лету.
    static readonly Dictionary<string, SolidColorBrush> brushes = new();
    static LinearGradientBrush? gradient;

    const int FadeMs = 400;

    static void SetBrush(string key, Color color, bool animate)
    {
        try
        {
            if (!brushes.TryGetValue(key, out var b) || b.IsFrozen)
            {
                b = new SolidColorBrush(color);
                brushes[key] = b;
                Application.Current.Resources[key] = b;
                return;
            }

            var old = b.Color;
            b.BeginAnimation(SolidColorBrush.ColorProperty, null);
            b.Color = color; // итоговый цвет ставим сразу, анимация — только украшение
            if (animate)
                b.BeginAnimation(SolidColorBrush.ColorProperty,
                    new ColorAnimation(old, color, TimeSpan.FromMilliseconds(FadeMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        catch (Exception ex)
        {
            Settings.Log("theme brush " + key + ": " + ex.Message);
            var nb = new SolidColorBrush(color);
            brushes[key] = nb;
            Application.Current.Resources[key] = nb;
        }
    }

    static void FadeStop(GradientStop stop, Color color)
    {
        var old = stop.Color;
        stop.BeginAnimation(GradientStop.ColorProperty, null);
        stop.Color = color;
        stop.BeginAnimation(GradientStop.ColorProperty,
            new ColorAnimation(old, color, TimeSpan.FromMilliseconds(FadeMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    public static void Apply(string id)
    {
        var t = All.FirstOrDefault(x => x.Id == id) ?? All[0];
        bool animate = brushes.Count > 0; // первая установка — без анимации
        Current = t;

        SetBrush("BgBrush", C(t.Bg), animate);
        SetBrush("CardBrush", C(t.Card), animate);
        SetBrush("CardBorderBrush", C(t.Dark ? "#33FFFFFF" : "#22000000"), animate);
        SetBrush("TextBrush", C(t.Text), animate);
        SetBrush("MutedBrush", C(t.Muted), animate);
        SetBrush("AccentBrush", C(t.Accent), animate);
        SetBrush("Accent2Brush", C(t.Accent2), animate);

        var hover = C(t.Accent);
        hover.A = 0x40;
        SetBrush("HoverBrush", hover, animate);

        try
        {
            if (gradient == null || gradient.IsFrozen)
            {
                gradient = new LinearGradientBrush(C(t.Accent), C(t.Accent2), 0);
                Application.Current.Resources["AccentGradBrush"] = gradient;
            }
            else
            {
                FadeStop(gradient.GradientStops[0], C(t.Accent));
                FadeStop(gradient.GradientStops[1], C(t.Accent2));
            }
        }
        catch (Exception ex)
        {
            Settings.Log("theme gradient: " + ex.Message);
            gradient = new LinearGradientBrush(C(t.Accent), C(t.Accent2), 0);
            Application.Current.Resources["AccentGradBrush"] = gradient;
        }

        Settings.Log("theme -> " + t.Id);
        try { Changed?.Invoke(); }
        catch (Exception ex) { Settings.Log("theme changed handler: " + ex.Message); }
    }
}
