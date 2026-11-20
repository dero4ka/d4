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
        new("scarlet", "Алый",    true,  "#1A0508", "#CC2A0A10", "#FFEFEF", "#D9A9AE", "#FF3B4E", "#FF8A4C",
            "#2A0710", "#C42B3C", "#FF8A5B", "#8A1A2B", "#5A1020", "#2E0913", "#FFD7B8", 0.3),
        new("crimson", "Багровый", true, "#12030A", "#CC24060F", "#FFEDF0", "#C99AA6", "#E11D48", "#FF6B8A",
            "#1A0310", "#7A0F2E", "#E11D48", "#5C0B22", "#3B0716", "#1F030C", "#FFB4C2", 0.8),
        new("ruby",   "Рубин",   true,  "#1B0A12", "#CC2B0F1D", "#FFEEF3", "#D0A3B4", "#FF5C7A", "#C45BFF",
            "#2A0B1C", "#9B2257", "#FF6F91", "#6B1A45", "#46102F", "#260819", "#FFD1DC", 0.5),
        new("light",  "Светлая", false, "#EAF1FC", "#CCFFFFFF", "#16203A", "#55627D", "#2F6BFF", "#7A4DFF",
            "#7FB0FF", "#BCD7FF", "#F4F8FF", "#6F94D6", "#5578B8", "#3D5C96", "#FFF6C9", 0.0),
    };

    public static Theme Current { get; private set; } = All[0];

    /// <summary>Срабатывает после смены темы.</summary>
    public static event Action? Changed;

    /// <summary>Режим стекла: прозрачные панели с белой кромкой, как в iOS.</summary>
    public static bool Glass { get; private set; }

    public static void InitGlass(bool on) => Glass = on;

    public static void SetGlass(bool on)
    {
        if (Glass == on) return;
        Glass = on;
        Apply(Current.Id);
    }

    static Color Lerp(Color a, Color b, double k) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * k), (byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));

    static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    // Кисти создаются один раз и дальше только плавно меняют цвет — все окна перекрашиваются на лету.
    static readonly Dictionary<string, SolidColorBrush> brushes = new();
    static LinearGradientBrush? gradient;
    static readonly Dictionary<string, LinearGradientBrush> grads = new();

    static void SetGrad(string key, Color a, Color b, bool animate)
    {
        try
        {
            if (!grads.TryGetValue(key, out var g) || g.IsFrozen)
            {
                g = new LinearGradientBrush(a, b, 0) { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                grads[key] = g;
                Application.Current.Resources[key] = g;
                return;
            }
            Fade(g.GradientStops[0], a, animate);
            Fade(g.GradientStops[1], b, animate);
        }
        catch (Exception ex)
        {
            Settings.Log("theme gradient " + key + ": " + ex.Message);
            var ng = new LinearGradientBrush(a, b, 0) { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            grads[key] = ng;
            Application.Current.Resources[key] = ng;
        }
    }

    static void Fade(GradientStop stop, Color color, bool animate)
    {
        var old = stop.Color;
        stop.BeginAnimation(GradientStop.ColorProperty, null);
        stop.Color = color;
        if (animate)
            stop.BeginAnimation(GradientStop.ColorProperty,
                new ColorAnimation(old, color, TimeSpan.FromMilliseconds(FadeMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

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

        // фон и карточки — всегда градиенты (второй цвет подмешивается из акцента темы)
        var bg = C(t.Bg);
        var bg2 = Lerp(bg, C(t.Accent2), t.Dark ? 0.22 : 0.18);
        var card = C(t.Card);
        var card2 = Alpha(Lerp(card, C(t.Accent), 0.14), card.A);

        if (Glass)
        {
            // стекло: сквозь панели виден рабочий стол, кромка светится
            SetGrad("BgBrush", Alpha(bg, (byte)(t.Dark ? 0x9A : 0xB8)), Alpha(bg2, (byte)(t.Dark ? 0x72 : 0x90)), animate);
            SetGrad("CardBrush", t.Dark ? C("#3CFFFFFF") : C("#A0FFFFFF"), t.Dark ? C("#16FFFFFF") : C("#58FFFFFF"), animate);
            SetGrad("CardBorderBrush", t.Dark ? C("#B0FFFFFF") : C("#F0FFFFFF"), t.Dark ? C("#24FFFFFF") : C("#60FFFFFF"), animate);
        }
        else
        {
            SetGrad("BgBrush", bg, bg2, animate);
            SetGrad("CardBrush", card, card2, animate);
            var edge = C(t.Dark ? "#33FFFFFF" : "#22000000");
            SetGrad("CardBorderBrush", edge, edge, animate);
        }
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
