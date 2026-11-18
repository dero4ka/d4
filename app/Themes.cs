using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

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

    /// <summary>Срабатывает после смены темы (например, чтобы обои перерисовали сцену).</summary>
    public static event Action? Changed;

    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush(C(hex));
        b.Freeze();
        return b;
    }

    public static void Apply(string id)
    {
        var t = All.FirstOrDefault(x => x.Id == id) ?? All[0];
        Current = t;
        var res = Application.Current.Resources;

        res["BgBrush"] = Brush(t.Bg);
        res["CardBrush"] = Brush(t.Card);
        res["CardBorderBrush"] = Brush(t.Dark ? "#33FFFFFF" : "#22000000");
        res["TextBrush"] = Brush(t.Text);
        res["MutedBrush"] = Brush(t.Muted);
        res["AccentBrush"] = Brush(t.Accent);
        res["Accent2Brush"] = Brush(t.Accent2);

        var hover = C(t.Accent);
        hover.A = 0x40;
        var hb = new SolidColorBrush(hover);
        hb.Freeze();
        res["HoverBrush"] = hb;

        var grad = new LinearGradientBrush(C(t.Accent), C(t.Accent2), 0);
        grad.Freeze();
        res["AccentGradBrush"] = grad;

        Changed?.Invoke();
    }
}
