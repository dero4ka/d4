using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace D4;

/// <summary>
/// Кнопка «Пуск» со своим логотипом для каждой темы: океан, закат, лес, неон, светлая.
/// Логотип перерисовывается при смене темы, цвета берутся из палитры темы.
/// </summary>
public sealed class StartButton : Border
{
    public event Action? Click;

    readonly Viewbox view = new() { Width = 22, Height = 22, Stretch = Stretch.Uniform };
    readonly ScaleTransform pop = new(1, 1);
    string shown = "";

    public StartButton()
    {
        view.RenderTransform = pop;
        view.RenderTransformOrigin = new Point(.5, .5);
        Child = view;
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(7);
        Margin = new Thickness(2);
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;

        MouseEnter += (_, _) => this.Dyn(BackgroundProperty, "HoverBrush");
        MouseLeave += (_, _) => Background = Brushes.Transparent;
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Click?.Invoke();
        };

        Loaded += (_, _) => ThemeManager.Changed += Rebuild;
        Unloaded += (_, _) => ThemeManager.Changed -= Rebuild;
        Rebuild();
    }

    void Rebuild()
    {
        var id = ThemeManager.Current.Id;
        if (id == shown) return;
        bool first = shown == "";
        shown = id;
        view.Child = Build(id);

        if (first) return;
        // при смене темы логотип «выскакивает» заново
        pop.ScaleX = 0.5;
        pop.ScaleY = 0.5;
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 };
        pop.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
        pop.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
    }

    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Прямоугольник со своим радиусом у каждого угла (в пикселях холста 24x24).</summary>
    static string Rr(double x, double y, double w, double h, double tl, double tr, double br, double bl) =>
        $"M{F(x + tl)},{F(y)} H{F(x + w - tr)} A{F(tr)},{F(tr)} 0 0 1 {F(x + w)},{F(y + tr)} " +
        $"V{F(y + h - br)} A{F(br)},{F(br)} 0 0 1 {F(x + w - br)},{F(y + h)} " +
        $"H{F(x + bl)} A{F(bl)},{F(bl)} 0 0 1 {F(x)},{F(y + h - bl)} " +
        $"V{F(y + tl)} A{F(tl)},{F(tl)} 0 0 1 {F(x + tl)},{F(y)} Z";

    static UIElement Build(string id)
    {
        var c = new Canvas { Width = 24, Height = 24 };

        void Fill(string data, string brushKey, double opacity = 1)
        {
            var p = new System.Windows.Shapes.Path { Data = Geometry.Parse(data), Opacity = opacity };
            p.SetResourceReference(Shape.FillProperty, brushKey);
            c.Children.Add(p);
        }

        void Outline(string data, string brushKey)
        {
            var p = new System.Windows.Shapes.Path { Data = Geometry.Parse(data), StrokeThickness = 1.6, Fill = Brushes.Transparent };
            p.SetResourceReference(Shape.StrokeProperty, brushKey);
            c.Children.Add(p);
        }

        // четыре «окна» 9x9 с зазором
        const double a = 2, b = 13, s = 9;

        switch (id)
        {
            case "sunset": // солнце: внешние углы скруглены так, что панели складываются в круг
                Fill(Rr(a, a, s, s, 8, 1, 1, 1), "AccentBrush");
                Fill(Rr(b, a, s, s, 1, 8, 1, 1), "Accent2Brush");
                Fill(Rr(a, b, s, s, 1, 1, 1, 8), "Accent2Brush");
                Fill(Rr(b, b, s, s, 1, 1, 8, 1), "AccentBrush");
                break;

            case "forest": // клевер: у каждой панели скруглены противоположные углы, как у листа
                Fill(Rr(a, a, s, s, 8, 1, 8, 1), "AccentBrush");
                Fill(Rr(b, a, s, s, 1, 8, 1, 8), "Accent2Brush");
                Fill(Rr(a, b, s, s, 1, 8, 1, 8), "Accent2Brush");
                Fill(Rr(b, b, s, s, 8, 1, 8, 1), "AccentBrush");
                break;

            case "neon": // контуры со светящейся серединой
            {
                var panes = new (double X, double Y, string Key)[]
                {
                    (a, a, "AccentBrush"), (b, a, "Accent2Brush"), (a, b, "Accent2Brush"), (b, b, "AccentBrush"),
                };
                foreach (var (x, y, key) in panes)
                {
                    Outline(Rr(x + 0.8, y + 0.8, s - 1.6, s - 1.6, 1.5, 1.5, 1.5, 1.5), key);
                    Fill(Rr(x + 3, y + 3, s - 6, s - 6, 0.6, 0.6, 0.6, 0.6), key, 0.55);
                }
                c.Effect = new DropShadowEffect
                {
                    Color = ThemeManager.C(ThemeManager.Current.Accent2),
                    BlurRadius = 7,
                    ShadowDepth = 0,
                    Opacity = 0.9,
                };
                break;
            }

            case "light": // плоский светлый логотип с лёгкой градацией
                Fill(Rr(a, a, s, s, 1.5, 1.5, 1.5, 1.5), "AccentBrush");
                Fill(Rr(b, a, s, s, 1.5, 1.5, 1.5, 1.5), "AccentBrush", 0.8);
                Fill(Rr(a, b, s, s, 1.5, 1.5, 1.5, 1.5), "AccentBrush", 0.8);
                Fill(Rr(b, b, s, s, 1.5, 1.5, 1.5, 1.5), "AccentBrush", 0.6);
                break;

            default: // океан: мягкие скруглённые панели в двух оттенках
                Fill(Rr(a, a, s, s, 3.5, 3.5, 3.5, 3.5), "AccentBrush");
                Fill(Rr(b, a, s, s, 3.5, 3.5, 3.5, 3.5), "Accent2Brush");
                Fill(Rr(a, b, s, s, 3.5, 3.5, 3.5, 3.5), "Accent2Brush");
                Fill(Rr(b, b, s, s, 3.5, 3.5, 3.5, 3.5), "AccentBrush");
                break;
        }

        return c;
    }
}
