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
        new("aurora", "Аврора",  true,  "#06141C", "#CC0B2530", "#E9FFF8", "#9CC9C0", "#5CF2C0", "#8F7BFF",
            "#040B16", "#0B3B4A", "#2A7A7A", "#0F3A44", "#0A2630", "#06151C", "#BFFFE8", 1.0),
        new("sakura", "Сакура",  false, "#FFF0F5", "#CCFFFFFF", "#3A1B2B", "#8A5A6E", "#FF5C93", "#FF9BC2",
            "#FFD0E3", "#FFB3D1", "#FFEAF2", "#E88DB0", "#D1678F", "#A8466B", "#FFFFFF", 0.0),
        new("cosmos", "Космос",  true,  "#060612", "#CC120C30", "#EEEBFF", "#A9A4D6", "#7C8CFF", "#FF6AD5",
            "#05050F", "#1A1250", "#4B1D7A", "#241A5C", "#150F3A", "#0A0720", "#E7DFFF", 1.0),
        new("winter", "Зима",    true,  "#0D1B2A", "#CC13283F", "#F0F8FF", "#A3BCD3", "#8EE3FF", "#C9B6FF",
            "#0B1A2E", "#35608F", "#BBD9F2", "#6E93BA", "#456E99", "#2A4A6E", "#F2FAFF", 0.4),
        new("storm",  "Гроза",   true,  "#0B1017", "#CC141C28", "#E8F0FA", "#98A8BC", "#5AB0FF", "#9AA8FF",
            "#0A0F18", "#1F2A3A", "#3E5068", "#2A384A", "#1B2634", "#0F1620", "#BFD3EA", 0.0),
        new("light",  "Светлая", false, "#EAF1FC", "#CCFFFFFF", "#16203A", "#55627D", "#2F6BFF", "#7A4DFF",
            "#7FB0FF", "#BCD7FF", "#F4F8FF", "#6F94D6", "#5578B8", "#3D5C96", "#FFF6C9", 0.0),
        new("synthwave", "Синтвейв", true, "#12062B", "#CC2A0D3C", "#FFF0FB", "#A597AC", "#FF4FD8", "#3DE6FF",
            "#1A0840", "#6A1B9A", "#FF6AC2", "#3B1A6B", "#29114E", "#1C0B3B", "#FFD36B", 1.0),
        new("desert", "Пустыня", true, "#2A150C", "#CC3F2414", "#FFF3E6", "#AE9F93", "#FFB05C", "#FF7A45",
            "#3A1A24", "#C2603A", "#FFC27A", "#8A4A2E", "#5F321F", "#422214", "#FFE3A8", 0.3),
        new("midnight", "Полночь", true, "#050B1C", "#CC101B33", "#EAF0FF", "#9399A9", "#6FA8FF", "#B9C7FF",
            "#030818", "#0E1F4D", "#2A4A8A", "#14295A", "#0D1C3E", "#09122C", "#EAF1FF", 1.0),
        new("mint", "Мята", false, "#EAFBF3", "#CCFFFFFF", "#12372B", "#648177", "#1FBF8A", "#5CD6C2",
            "#9BE8CF", "#C9F5E4", "#F2FFF9", "#6CC9A8", "#59A58A", "#46836D", "#FFFBD6", 0),
        new("lavender", "Лаванда", false, "#F4EFFF", "#CCFFFFFF", "#2A1B4D", "#776C91", "#8F6BFF", "#C58BFF",
            "#CBB8FF", "#E2D6FF", "#F8F4FF", "#9E86E0", "#826EB8", "#675792", "#FFFFFF", 0),
        new("gold", "Золото", true, "#1A1405", "#CC31270C", "#FFF8E0", "#A8A18D", "#FFD24A", "#FFA62B",
            "#241A06", "#8A6A12", "#FFD86B", "#6B5210", "#47360B", "#2E2408", "#FFF2B8", 0.6),
        new("ice", "Лёд", false, "#EAF6FF", "#CCFFFFFF", "#10304D", "#637B91", "#2EA8FF", "#7AD8FF",
            "#8FD0FF", "#C4E6FF", "#F0FAFF", "#7FAED6", "#688FAF", "#53718B", "#FFFFFF", 0),
        new("volcano", "Вулкан", true, "#150605", "#CC2C0E08", "#FFEDE3", "#A6958F", "#FF5A1F", "#FFB02E",
            "#1E0A08", "#7A1E10", "#FF7A3A", "#4A1610", "#320F0B", "#220A08", "#FFD27A", 0.3),
        new("toxic", "Токсик", true, "#071207", "#CC142A0C", "#EFFFE3", "#97A58F", "#8CFF3D", "#EFFF3D",
            "#051005", "#1A4A12", "#5FD13A", "#163A12", "#0F280D", "#0B1C0A", "#DFFFB0", 0.5),
        new("cyber", "Киберпанк", true, "#0B0716", "#CC231D18", "#FFFBE0", "#A29E93", "#FFE62E", "#2EF2FF",
            "#0A0620", "#3A1A6E", "#FF3D9A", "#2A1752", "#1C1037", "#130B25", "#FFD6F0", 0.6),
        new("coral", "Коралл", true, "#10202A", "#CC282930", "#FFF1EB", "#A4A2A2", "#FF7A6B", "#FFB36B",
            "#0F2A3A", "#2A7A8A", "#FFB59A", "#1F5A6A", "#18404D", "#142E3A", "#FFE3C2", 0.3),
        new("lagoon", "Лагуна", true, "#06232A", "#CC0A383B", "#E8FFFB", "#92ABAC", "#2EF2D0", "#59B8FF",
            "#06323F", "#1A8A9A", "#9AF5E8", "#13606E", "#0D454F", "#09323B", "#F0FFFB", 0.4),
        new("autumn", "Осень", true, "#1F1208", "#CC35200D", "#FFF1E0", "#AA9C8E", "#FF9A3D", "#E8571F",
            "#2A1A0C", "#A8501F", "#FFC27A", "#7A3A16", "#512810", "#361C0C", "#FFE0A0", 0.2),
        new("spring", "Весна", false, "#F2FFE9", "#CCFFFFFF", "#183A18", "#6B8567", "#4CC24A", "#FF8FC0",
            "#A8E8A0", "#D4F5C0", "#F8FFEF", "#78C26E", "#629F5A", "#4E7E48", "#FFFBC0", 0),
        new("graphite", "Графит", true, "#0E0F12", "#CC1C1E23", "#ECEFF4", "#989A9E", "#9AA6B8", "#6F8BFF",
            "#0A0B0E", "#2A2F38", "#5A6372", "#2E333D", "#20232A", "#16181D", "#D8DEE8", 0.3),
        new("nebula", "Туманность", true, "#0A0618", "#CC1B0F2F", "#F3EBFF", "#9A94A7", "#B05CFF", "#FF5C9E",
            "#070412", "#2A1060", "#6A2A9A", "#1E1050", "#150C37", "#0F0826", "#FFE3F5", 1.0),
        new("candy", "Конфетти", false, "#FFF0FA", "#CCFFFFFF", "#4A1A3A", "#8F6B83", "#FF4FA8", "#4FC8FF",
            "#FFC8EC", "#FFE0F4", "#FFF8FD", "#F08ACB", "#C571A6", "#9C5A84", "#FFFFFF", 0),
        new("matrix", "Матрица", true, "#020A04", "#CC06220E", "#D8FFE4", "#87A28F", "#2EFF6A", "#B8FF2E",
            "#010802", "#0A3A1A", "#1FA84A", "#0A3015", "#061F0D", "#041408", "#B8FFC8", 0.5),
        new("sepia", "Сепия", false, "#F5ECDD", "#CCFFFFFF", "#3A2410", "#81705E", "#B5651D", "#D99A4B",
            "#E8CFA6", "#F2E2C4", "#FBF4E6", "#C79A62", "#A37E50", "#816440", "#FFF6D6", 0),
        new("rose", "Розовый кварц", false, "#FFEFF2", "#CCFFFFFF", "#4A1A2A", "#8F6B76", "#E94B7A", "#FF9BB5",
            "#FFC5D4", "#FFE0E8", "#FFF6F8", "#E69AB0", "#BD7E90", "#966472", "#FFFFFF", 0),
        new("jungle", "Джунгли", true, "#04140A", "#CC082914", "#E8FFEF", "#91A698", "#2EE66A", "#E6FF2E",
            "#06210F", "#0F5A2A", "#5FD17A", "#0E4A24", "#0A3218", "#062210", "#E6FFC0", 0.4),
        new("steel", "Сталь", true, "#0A1118", "#CC14222F", "#EAF2FA", "#959CA4", "#6FB8FF", "#C8D6E6",
            "#08101A", "#1F3A5A", "#6F90B0", "#1A2E44", "#132130", "#0E1823", "#E8F2FF", 0.3),
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
            // стекло окрашено в цвета темы: фон и кромка берут оттенок акцента
            var acc = C(t.Accent);
            var acc2 = C(t.Accent2);
            var tintA = Lerp(bg, acc, 0.28);
            var tintB = Lerp(bg2, acc2, 0.30);
            SetGrad("BgBrush", Alpha(tintA, (byte)(t.Dark ? 0x4A : 0x78)), Alpha(tintB, (byte)(t.Dark ? 0x30 : 0x58)), animate);
            SetGrad("CardBrush", Alpha(Lerp(acc, Colors.White, t.Dark ? .15 : .55), (byte)(t.Dark ? 0x30 : 0x6A)), Alpha(acc2, (byte)(t.Dark ? 0x12 : 0x30)), animate);
            SetGrad("CardBorderBrush", Alpha(Lerp(acc, Colors.White, .62), 0xE0), Alpha(acc2, (byte)(t.Dark ? 0x40 : 0x70)), animate);
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
        if (Glass) hover = Alpha(Lerp(C(t.Accent), Colors.White, .35), (byte)(t.Dark ? 0x40 : 0x66));
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
