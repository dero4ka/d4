using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace D4;

/// <summary>
/// Рисует пейзаж темы из векторных фигур (холст 800x450, растягивается под экран).
/// Те же композиции, что и на сайте: океан, закат, лес, неон, светлая.
/// </summary>
public static class SceneFactory
{
    static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    static SolidColorBrush Br(string hex)
    {
        var b = new SolidColorBrush(ThemeManager.C(hex));
        b.Freeze();
        return b;
    }

    static Shape Path(string d, string hex, double opacity = 1) =>
        new System.Windows.Shapes.Path { Data = Geometry.Parse(d), Fill = Br(hex), Opacity = opacity };

    static Shape Circle(double cx, double cy, double r, string hex, double opacity = 1) => Ellipse(cx, cy, r, r, hex, opacity);

    static Shape Ellipse(double cx, double cy, double rx, double ry, string hex, double opacity = 1)
    {
        var e = new System.Windows.Shapes.Ellipse { Width = rx * 2, Height = ry * 2, Fill = Br(hex), Opacity = opacity };
        Canvas.SetLeft(e, cx - rx);
        Canvas.SetTop(e, cy - ry);
        return e;
    }

    static Shape Box(double x, double y, double w, double h, string hex, double opacity = 1)
    {
        var r = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Fill = Br(hex), Opacity = opacity };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        return r;
    }

    static Shape Line(double x1, double y1, double x2, double y2, string hex, double opacity)
    {
        return new System.Windows.Shapes.Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = Br(hex), StrokeThickness = 1.5, Opacity = opacity,
        };
    }

    static string Pines(double baseY, double h, double w, int n, double off)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            double x = (i + 0.5) * 800.0 / n + (i % 2 == 1 ? off : -off);
            sb.Append($"M{F(x - w / 2)} {F(baseY)}L{F(x)} {F(baseY - h * .62)}L{F(x + w / 2)} {F(baseY)}Z ");
            sb.Append($"M{F(x - w * .36)} {F(baseY - h * .38)}L{F(x)} {F(baseY - h)}L{F(x + w * .36)} {F(baseY - h * .38)}Z ");
        }
        return sb.ToString();
    }

    static void Cloud(Canvas c, double x, double y, double s)
    {
        c.Children.Add(Ellipse(x, y, 40 * s, 14 * s, "#FFFFFF", .85));
        c.Children.Add(Ellipse(x + 26 * s, y - 10 * s, 30 * s, 16 * s, "#FFFFFF", .85));
        c.Children.Add(Ellipse(x - 26 * s, y - 4 * s, 28 * s, 12 * s, "#FFFFFF", .85));
    }

    public static UIElement Build(Theme t)
    {
        var canvas = new Canvas { Width = 800, Height = 450, ClipToBounds = true };

        // небо
        var sky = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(ThemeManager.C(t.S1), 0),
                new GradientStop(ThemeManager.C(t.S2), .58),
                new GradientStop(ThemeManager.C(t.S3), 1),
            },
            new Point(0, 0), new Point(0, 1));
        canvas.Children.Add(new System.Windows.Shapes.Rectangle { Width = 800, Height = 450, Fill = sky });

        if (t.Stars > 0)
        {
            double[][] pts =
            {
                new[] { 60.0, 40 }, new[] { 140.0, 90 }, new[] { 230.0, 30 }, new[] { 330.0, 70 }, new[] { 420.0, 25 },
                new[] { 520.0, 85 }, new[] { 610.0, 40 }, new[] { 700.0, 75 }, new[] { 760.0, 20 }, new[] { 90.0, 150 },
                new[] { 300.0, 120 }, new[] { 500.0, 130 }, new[] { 660.0, 140 }, new[] { 370.0, 50 }, new[] { 450.0, 100 },
            };
            foreach (var p in pts) canvas.Children.Add(Circle(p[0], p[1], 1.6, "#FFFFFF", .8 * t.Stars));
        }

        var add = (Shape s) => canvas.Children.Add(s);

        switch (t.Id)
        {
            case "ocean":
                add(Circle(400, 150, 95, t.Sun, .25));
                add(Circle(400, 150, 42, t.Sun));
                add(Path("M0 300 L120 238 L220 290 L330 228 L450 295 L560 250 L680 300 L800 255 V330 H0Z", t.M1));
                add(Box(0, 300, 800, 150, t.M2));
                foreach (var (w, y) in new[] { (96, 312), (70, 324), (104, 336), (60, 349), (84, 363), (46, 378) })
                    add(Box(400 - w / 2.0, y, w, 3, t.Sun, .55));
                add(Path("M0 450 L0 395 Q90 365 170 410 L260 450Z", t.M3));
                add(Path("M800 450 L800 385 Q700 355 620 415 L560 450Z", t.M3));
                break;

            case "sunset":
                add(Circle(400, 300, 150, t.Sun, .25));
                add(Circle(400, 300, 96, t.Sun));
                add(Ellipse(250, 170, 100, 7, "#FFFFFF", .4));
                add(Ellipse(540, 215, 120, 8, "#FFFFFF", .35));
                add(Ellipse(390, 135, 70, 6, "#FFFFFF", .4));
                add(Ellipse(160, 235, 80, 5, "#FFFFFF", .3));
                add(Path("M0 305 L0 245 L80 268 L165 228 L270 305Z", t.M1));
                add(Path("M800 305 L800 238 L710 262 L625 222 L520 305Z", t.M2));
                add(Box(0, 300, 800, 150, t.M3));
                foreach (var (w, y) in new[] { (200, 312), (150, 328), (110, 345), (70, 363), (40, 382) })
                    add(Box(400 - w / 2.0, y, w, 4, t.Sun, .6));
                add(Path("M0 450 L0 410 Q120 385 230 450Z", t.M2));
                add(Path("M800 450 L800 400 Q680 372 560 450Z", t.M2));
                break;

            case "forest":
                add(Circle(400, 110, 150, t.Sun, .25));
                add(Circle(400, 110, 34, t.Sun, .7));
                add(Path("M375 0 L425 0 L590 450 L300 450Z", t.Sun, .12));
                add(Path("M330 0 L360 0 L260 450 L140 450Z", t.Sun, .08));
                add(Path("M0 320 Q150 250 300 300 T560 290 T800 310 V450 H0Z", t.M1));
                add(Path(Pines(340, 100, 46, 15, 9), t.M1));
                add(Ellipse(400, 330, 420, 26, t.Sun, .12));
                add(Path(Pines(395, 130, 60, 11, 12), t.M2));
                add(Ellipse(300, 385, 380, 20, t.Sun, .1));
                add(Path(Pines(460, 175, 80, 8, 16), t.M3));
                break;

            case "neon":
            {
                add(Circle(400, 235, 130, t.Sun, .25));
                add(Circle(400, 235, 92, t.Sun));
                var b = new[]
                {
                    new[] { 40, 60, 90 }, new[] { 110, 50, 130 }, new[] { 170, 70, 100 }, new[] { 250, 60, 170 },
                    new[] { 320, 70, 120 }, new[] { 395, 60, 210 }, new[] { 465, 70, 140 }, new[] { 540, 60, 180 },
                    new[] { 610, 70, 110 }, new[] { 685, 60, 150 }, new[] { 745, 70, 95 },
                };
                foreach (var q in b) add(Box(q[0], 330 - q[2], q[1], q[2], t.M2));
                add(Box(0, 330, 800, 120, t.M3));
                for (int i = 0; i < b.Length; i++)
                    for (int k = 0; k < 4; k++)
                        if ((i + k) % 2 == 0)
                            add(Box(b[i][0] + 8 + (k % 2) * (b[i][1] / 2.0 - 4), 330 - b[i][2] + 14 + (k / 2) * (b[i][2] / 3.0), 6, 8, t.Sun, .75));
                foreach (var y in new[] { 345, 362, 385, 415 }) add(Line(0, y, 800, y, t.Accent2, .6));
                for (int i = -7; i <= 7; i++) add(Line(400, 330, 400 + i * 150, 450, t.Accent2, .5));
                break;
            }

            default: // light
                add(Circle(620, 105, 80, t.Sun, .25));
                add(Circle(620, 105, 38, t.Sun));
                Cloud(canvas, 330, 105, 1.2);
                Cloud(canvas, 470, 210, 1);
                Cloud(canvas, 190, 175, .9);
                Cloud(canvas, 680, 230, .8);
                add(Path("M0 330 Q200 268 400 322 T800 298 V450 H0Z", t.M1));
                add(Path("M0 385 Q250 332 450 378 T800 350 V450 H0Z", t.M2));
                add(Path("M0 435 Q300 388 520 428 T800 405 V450 H0Z", t.M3));
                break;
        }

        return new Viewbox { Stretch = Stretch.UniformToFill, Child = canvas };
    }
}
