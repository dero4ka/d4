using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace D4;

/// <summary>
/// Рисует обои темы векторно (холст 800x450, масштабируется под любой экран без потери качества):
/// градиентные слои, свечение, дымка, отражения. Поверх — лёгкое зерно и виньетка, чтобы не было полос на градиентах.
/// </summary>
public static class SceneFactory
{
    static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    static Color C(string hex) => ThemeManager.C(hex);

    static Color Mix(string a, string b, double t) => Mix(C(a), C(b), t);

    static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    static Color A(Color c, double alpha) => Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);

    static Brush Solid(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>Вертикальный градиент сверху вниз.</summary>
    static Brush V(params (Color Color, double At)[] stops)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (c, o) in stops) g.GradientStops.Add(new GradientStop(c, o));
        g.Freeze();
        return g;
    }

    /// <summary>Круглое свечение: цвет в центре плавно уходит в прозрачность.</summary>
    static Brush Glow(Color c, double strength = .9)
    {
        var g = new RadialGradientBrush();
        g.GradientStops.Add(new GradientStop(A(c, strength), 0));
        g.GradientStops.Add(new GradientStop(A(c, strength * .35), .45));
        g.GradientStops.Add(new GradientStop(A(c, 0), 1));
        g.Freeze();
        return g;
    }

    static Shape P(string d, Brush b, double opacity = 1) =>
        new System.Windows.Shapes.Path { Data = Geometry.Parse(d), Fill = b, Opacity = opacity };

    static Shape E(double cx, double cy, double rx, double ry, Brush b, double opacity = 1)
    {
        var e = new System.Windows.Shapes.Ellipse { Width = rx * 2, Height = ry * 2, Fill = b, Opacity = opacity };
        Canvas.SetLeft(e, cx - rx);
        Canvas.SetTop(e, cy - ry);
        return e;
    }

    static Shape R(double x, double y, double w, double h, Brush b, double opacity = 1)
    {
        var r = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Fill = b, Opacity = opacity };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        return r;
    }

    static Shape L(double x1, double y1, double x2, double y2, Color c, double thickness = 1.2)
    {
        return new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Solid(c), StrokeThickness = thickness };
    }

    static readonly double[][] StarPts =
    {
        new[] { 60.0, 40, 1.4 }, new[] { 140.0, 90, 1.0 }, new[] { 230.0, 30, 1.8 }, new[] { 330.0, 70, 1.1 }, new[] { 420.0, 25, 1.5 },
        new[] { 520.0, 85, 1.0 }, new[] { 610.0, 40, 1.9 }, new[] { 700.0, 75, 1.2 }, new[] { 760.0, 20, 1.5 }, new[] { 90.0, 150, 1.1 },
        new[] { 300.0, 120, 1.0 }, new[] { 500.0, 130, 1.3 }, new[] { 660.0, 140, 1.0 }, new[] { 370.0, 50, 1.2 }, new[] { 450.0, 100, 1.0 },
        new[] { 25.0, 100, 1.0 }, new[] { 190.0, 140, 1.3 }, new[] { 570.0, 20, 1.1 }, new[] { 730.0, 125, 1.0 }, new[] { 270.0, 20, 1.0 },
    };

    static void Stars(Canvas c, double strength, Color tint)
    {
        if (strength <= 0) return;
        foreach (var p in StarPts)
        {
            c.Children.Add(E(p[0], p[1], p[2] * 5, p[2] * 5, Glow(tint, .35), strength));
            c.Children.Add(E(p[0], p[1], p[2], p[2], Solid(Colors.White), .9 * strength));
        }
    }

    /// <summary>Горная гряда с градиентом: светлый край сверху, тёмное основание.</summary>
    static Shape Ridge(string d, Color top, Color bottom, double opacity = 1) => P(d, V((top, 0), (bottom, 1)), opacity);

    /// <summary>Полоса дымки над горизонтом.</summary>
    static Shape Haze(double y, double h, Color c, double a)
    {
        return R(0, y, 800, h, V((A(c, 0), 0), (A(c, a), .6), (A(c, 0), 1)));
    }

    static string Pines(double baseY, double h, double w, int n, double off, int seed)
    {
        var sb = new StringBuilder();
        var rnd = new Random(seed);
        for (int i = 0; i < n; i++)
        {
            double x = (i + 0.5) * 800.0 / n + (i % 2 == 1 ? off : -off) + rnd.Next(-8, 9);
            double k = .85 + rnd.NextDouble() * .3;
            double hh = h * k, ww = w * k;
            sb.Append($"M{F(x - ww / 2)} {F(baseY)}L{F(x)} {F(baseY - hh * .6)}L{F(x + ww / 2)} {F(baseY)}Z ");
            sb.Append($"M{F(x - ww * .38)} {F(baseY - hh * .36)}L{F(x)} {F(baseY - hh * .86)}L{F(x + ww * .38)} {F(baseY - hh * .36)}Z ");
            sb.Append($"M{F(x - ww * .26)} {F(baseY - hh * .62)}L{F(x)} {F(baseY - hh)}L{F(x + ww * .26)} {F(baseY - hh * .62)}Z ");
        }
        return sb.ToString();
    }

    static void Cloud(Canvas c, double x, double y, double s, double a = .8)
    {
        var b = V((Colors.White, 0), (Colors.White, 1));
        c.Children.Add(E(x, y, 46 * s, 13 * s, Solid(Colors.White), a * .75));
        c.Children.Add(E(x + 28 * s, y - 10 * s, 32 * s, 17 * s, Solid(Colors.White), a));
        c.Children.Add(E(x - 28 * s, y - 5 * s, 30 * s, 13 * s, Solid(Colors.White), a));
        c.Children.Add(E(x, y + 6 * s, 56 * s, 8 * s, Solid(Color.FromRgb(0xCF, 0xE0, 0xFF)), a * .35));
    }

    /// <summary>Светящийся диск (солнце, луна) с ореолом.</summary>
    static void Orb(Canvas c, double x, double y, double r, Color core, Color edge, double glowRadius, double glowStrength = .9)
    {
        c.Children.Add(E(x, y, glowRadius, glowRadius, Glow(core, glowStrength)));
        c.Children.Add(E(x, y, r * 1.6, r * 1.6, Glow(core, .35)));
        c.Children.Add(E(x, y, r, r, V((core, 0), (edge, 1))));
    }

    static Canvas NewCanvas(Theme t, params (string Color, double At)[] extraSky)
    {
        var canvas = new Canvas { Width = 800, Height = 450, ClipToBounds = true };
        canvas.Children.Add(R(0, 0, 800, 450, V(
            (C(t.S1), 0),
            (Mix(t.S1, t.S2, .55), .3),
            (C(t.S2), .62),
            (C(t.S3), 1))));
        return canvas;
    }

    public static UIElement Build(Theme t)
    {
        var c = NewCanvas(t);
        var s1 = C(t.S1); var s2 = C(t.S2); var s3 = C(t.S3);
        var m1 = C(t.M1); var m2 = C(t.M2); var m3 = C(t.M3);
        var sun = C(t.Sun); var acc = C(t.Accent); var acc2 = C(t.Accent2);
        void Add(Shape s) => c.Children.Add(s);

        Stars(c, t.Stars, Mix(sun, Colors.White, .5));

        switch (t.Id)
        {
            case "ocean":
                Orb(c, 400, 150, 42, sun, Mix(sun, s3, .35), 240);
                Add(E(400, 150, 300, 40, Glow(sun, .25)));
                Add(Ridge("M0 285 L60 262 L120 275 L200 240 L260 270 L330 232 L400 262 L470 228 L560 268 L640 244 L720 270 L800 250 V330 H0Z", Mix(m1, s3, .45), Mix(m1, s2, .3), .75));
                Add(Ridge("M0 305 L90 262 L180 292 L270 250 L380 298 L470 262 L560 300 L680 258 L800 296 V340 H0Z", Mix(m1, s3, .2), m1));
                Add(Haze(260, 90, s3, .35));
                Add(R(0, 300, 800, 150, V((Mix(m2, s3, .25), 0), (m2, .35), (m3, 1))));
                for (int i = 0; i < 18; i++)
                {
                    double y = 304 + i * 6.2, w = 20 + (i < 9 ? i * 14 : (18 - i) * 12 + 40);
                    Add(R(400 - w / 2, y, w, 2 + i * .12, Solid(sun), .5 - i * .022));
                }
                for (int i = 0; i < 30; i++)
                    Add(R((i * 97 + 20) % 780, 312 + (i * 31) % 120, 14 + (i % 4) * 10, 1.2, Solid(Mix(sun, Colors.White, .4)), .18));
                Add(P("M0 450 L0 392 Q80 360 165 408 Q210 430 260 450Z", V((Mix(m3, s2, .15), 0), (m3, 1))));
                Add(P("M800 450 L800 380 Q710 350 625 410 Q590 432 555 450Z", V((Mix(m3, s2, .15), 0), (m3, 1))));
                break;

            case "sunset":
                Orb(c, 400, 300, 96, sun, Mix(sun, s3, .5), 380, 1);
                Add(E(400, 300, 380, 60, Glow(Mix(sun, s3, .4), .5)));
                foreach (var (x, y, w, a) in new[] { (250.0, 165.0, 130.0, .45), (545.0, 205.0, 160.0, .4), (390.0, 125.0, 100.0, .45), (150.0, 232.0, 110.0, .35), (660.0, 150.0, 90.0, .3) })
                {
                    Add(E(x, y, w, 8, V((Mix(sun, Colors.White, .4), 0), (Mix(s3, s2, .3), 1)), a));
                    Add(E(x + 30, y + 9, w * .7, 4, Solid(Mix(sun, Colors.White, .3)), a * .6));
                }
                Add(Ridge("M0 308 L0 248 L80 270 L165 230 L270 308Z", Mix(m1, s3, .45), m1, .9));
                Add(Ridge("M800 308 L800 240 L710 264 L625 224 L520 308Z", Mix(m2, s3, .45), m2, .9));
                Add(Haze(250, 80, s3, .45));
                Add(R(0, 300, 800, 150, V((Mix(m3, s3, .35), 0), (m3, .45), (Mix(m3, Colors.Black, .35), 1))));
                for (int i = 0; i < 12; i++)
                {
                    double y = 306 + i * 11, w = 230 - i * 17;
                    if (w > 10) Add(R(400 - w / 2, y, w, 3 + i * .3, Solid(sun), .7 - i * .045));
                }
                Add(P("M0 450 L0 412 Q120 384 235 450Z", V((Mix(m2, Colors.Black, .1), 0), (Mix(m3, Colors.Black, .4), 1))));
                Add(P("M800 450 L800 402 Q680 372 555 450Z", V((Mix(m2, Colors.Black, .1), 0), (Mix(m3, Colors.Black, .4), 1))));
                break;

            case "forest":
                Orb(c, 400, 105, 32, sun, Mix(sun, s3, .3), 220, .8);
                foreach (var (x1, x2, x3, x4, a) in new[] { (370.0, 430.0, 640.0, 290.0, .16), (330.0, 360.0, 250.0, 120.0, .1), (430.0, 470.0, 760.0, 560.0, .08) })
                    Add(P($"M{F(x1)} 0 L{F(x2)} 0 L{F(x3)} 450 L{F(x4)} 450Z", V((sun, 0), (A(sun, 0), 1)), a * 2.2));
                Add(P("M0 330 Q150 255 300 305 T560 295 T800 312 V450 H0Z", V((Mix(m1, s3, .35), 0), (m1, 1))));
                Add(P(Pines(345, 105, 46, 17, 9, 3), V((Mix(m1, s3, .25), 0), (m1, 1))));
                Add(Haze(250, 130, Mix(sun, s3, .3), .35));
                Add(P(Pines(398, 135, 62, 12, 12, 7), V((Mix(m2, s3, .15), 0), (m2, 1))));
                Add(E(400, 338, 440, 28, Glow(sun, .35)));
                Add(Haze(330, 90, sun, .12));
                Add(P(Pines(462, 180, 84, 8, 16, 11), V((m3, 0), (Mix(m3, Colors.Black, .45), 1))));
                for (int i = 0; i < 26; i++)
                {
                    double fx = (i * 83 + 40) % 780, fy = 300 + (i * 47) % 140;
                    Add(E(fx, fy, 7, 7, Glow(acc2, .8)));
                    Add(E(fx, fy, 1.3, 1.3, Solid(Colors.White), .9));
                }
                break;

            case "neon":
                Orb(c, 400, 232, 92, Mix(sun, Colors.White, .2), acc2, 340, .8);
                {
                    var cut = Mix(s2, s3, .65);
                    for (int i = 0; i < 6; i++) Add(R(300, 252 + i * 12, 200, 2.5 + i * 1.3, Solid(cut)));
                }
                var b = new[]
                {
                    new[] { 30, 64, 96 }, new[] { 100, 52, 138 }, new[] { 160, 72, 104 }, new[] { 245, 62, 178 },
                    new[] { 315, 72, 124 }, new[] { 393, 62, 214 }, new[] { 462, 72, 144 }, new[] { 540, 62, 186 },
                    new[] { 610, 74, 112 }, new[] { 688, 60, 154 }, new[] { 748, 70, 98 },
                };
                foreach (var q in b)
                {
                    Add(R(q[0], 330 - q[2], q[1], q[2], V((Mix(m2, acc2, .25), 0), (m2, 1))));
                    Add(R(q[0], 330 - q[2], q[1], 2, Solid(acc2), .8));
                }
                Add(Haze(250, 90, acc2, .35));
                for (int i = 0; i < b.Length; i++)
                    for (int k = 0; k < 6; k++)
                        if ((i * 3 + k * 5) % 3 != 0)
                            Add(R(b[i][0] + 7 + (k % 2) * (b[i][1] / 2.0 - 3), 330 - b[i][2] + 14 + (k / 2) * (b[i][2] / 3.4), 6, 8, Solid((i + k) % 3 == 0 ? acc : sun), .85));
                Add(R(0, 330, 800, 120, V((Mix(m3, acc2, .2), 0), (m3, .3), (Mix(m3, Colors.Black, .3), 1))));
                foreach (var y in new[] { 338.0, 352, 372, 400, 438 })
                {
                    Add(L(0, y, 800, y, acc2, 3.2).Also(s => s.Opacity = .18));
                    Add(L(0, y, 800, y, acc2, 1.2).Also(s => s.Opacity = .85));
                }
                for (int i = -8; i <= 8; i++)
                {
                    Add(L(400, 330, 400 + i * 150, 450, acc2, 3.2).Also(s => s.Opacity = .15));
                    Add(L(400, 330, 400 + i * 150, 450, acc2, 1.1).Also(s => s.Opacity = .75));
                }
                break;

            case "scarlet":
            {
                Orb(c, 520, 245, 70, Mix(sun, Colors.White, .25), Mix(sun, acc2, .5), 360, 1);
                Add(E(520, 250, 400, 70, Glow(acc2, .45)));
                for (int i = 0; i < 5; i++)
                    Add(E(120 + i * 150, 120 + (i * 37) % 80, 150, 6, V((Mix(sun, acc2, .4), 0), (acc, 1)), .3));
                Add(Ridge("M0 300 L90 268 L170 292 L260 252 L340 288 L420 262 L800 300 V340 H0Z", Mix(m1, s3, .4), m1, .8));
                // вулкан со светящимся кратером
                Add(Ridge("M180 360 L330 215 L372 228 L392 214 L470 360Z", Mix(m2, acc2, .25), Mix(m2, Colors.Black, .3)));
                Add(E(382, 222, 30, 8, Glow(acc2, 1)));
                Add(P("M370 224 Q360 290 345 360 L355 360 Q372 292 382 228Z", V((acc2, 0), (A(acc, 0), 1)), .8));
                Add(P("M392 224 Q405 290 418 360 L428 360 Q412 292 395 226Z", V((acc2, 0), (A(acc, 0), 1)), .6));
                Add(Haze(230, 120, Mix(acc, s3, .5), .4));
                Add(Ridge("M0 450 L0 330 Q110 292 220 330 T450 322 T800 316 V450Z", Mix(m2, acc, .15), Mix(m3, Colors.Black, .35)));
                Add(Ridge("M0 450 L0 395 Q160 360 320 400 T640 392 T800 380 V450Z", m3, Mix(m3, Colors.Black, .55)));
                for (int i = 0; i < 40; i++)
                {
                    double x = (i * 61 + 17) % 800, y = 60 + (i * 53) % 330, r = 1 + (i % 3) * .6;
                    Add(E(x, y, r * 5, r * 5, Glow(acc2, .5), .8));
                    Add(E(x, y, r, r, Solid(Mix(sun, Colors.White, .4)), .9));
                }
                break;
            }

            case "crimson":
            {
                Orb(c, 320, 150, 72, Mix(acc, sun, .35), Mix(acc, m1, .3), 300, .55);
                foreach (var (dx, dy, r) in new[] { (-20.0, -10.0, 14.0), (18.0, 14.0, 10.0), (-4.0, 28.0, 8.0), (30.0, -22.0, 6.0) })
                    Add(E(320 + dx, 150 + dy, r, r, Solid(Mix(m1, Colors.Black, .3)), .22));
                Add(E(300, 128, 130, 5, V((s1, 0), (A(s1, .9), 1)), .6));
                Add(E(360, 168, 110, 4, Solid(s1), .5));
                Add(Ridge("M0 292 L70 238 L130 272 L210 215 L290 268 L380 232 L470 278 L560 222 L650 270 L740 236 L800 262 V340 H0Z", Mix(m1, acc, .22), m1, .85));
                Add(Haze(230, 100, acc, .3));
                Add(Ridge("M0 330 L60 280 L140 322 L230 262 L330 326 L430 276 L530 330 L640 270 L730 322 L800 296 V360 H0Z", Mix(m2, acc, .15), m2));
                Add(R(0, 318, 800, 132, V((Mix(m3, acc, .3), 0), (m3, .4), (Mix(m3, Colors.Black, .4), 1))));
                for (int i = 0; i < 16; i++)
                {
                    double y = 324 + i * 7, w = 14 + Math.Sin(i * .5) * 20 + 80 - Math.Abs(i - 6) * 3;
                    Add(R(320 - w / 2 + (i % 3) * 3, y, w, 2, Solid(Mix(acc, sun, .35)), .5 - i * .025));
                }
                Add(P("M0 450 L0 410 L60 382 L120 418 L190 392 L260 450Z", Solid(Mix(m3, Colors.Black, .5))));
                Add(P("M800 450 L800 392 L740 366 L690 404 L620 380 L560 450Z", Solid(Mix(m3, Colors.Black, .5))));
                break;
            }

            default: // light и ruby — остальные ниже
                break;
        }

        if (t.Id == "ruby")
        {
            Orb(c, 400, 190, 60, Mix(sun, Colors.White, .4), Mix(sun, s3, .5), 330, .9);
            Add(Ridge("M0 300 L70 250 L130 290 L210 215 L280 285 L350 240 L430 290 L520 225 L600 285 L680 238 L760 280 L800 255 V340 H0Z", Mix(m1, s3, .5), Mix(m1, s2, .2), .8));
            // грани кристаллических скал
            Add(P("M60 400 L150 250 L230 400Z", V((Mix(m2, s3, .55), 0), (m2, 1))));
            Add(P("M150 250 L230 400 L150 400Z", Solid(Mix(m2, Colors.Black, .35)), .75));
            Add(P("M520 410 L640 205 L740 410Z", V((Mix(m2, s3, .55), 0), (m2, 1))));
            Add(P("M640 205 L740 410 L640 410Z", Solid(Mix(m2, Colors.Black, .35)), .75));
            Add(P("M300 420 L380 300 L450 420Z", V((Mix(m2, s3, .45), 0), (m2, 1))));
            Add(P("M380 300 L450 420 L380 420Z", Solid(Mix(m2, Colors.Black, .35)), .75));
            Add(Haze(270, 110, s3, .4));
            Add(Ridge("M0 450 L0 385 Q140 350 300 392 T620 386 T800 372 V450Z", Mix(m3, acc, .2), Mix(m3, Colors.Black, .45)));
            for (int i = 0; i < 22; i++)
            {
                double x = (i * 71 + 25) % 790, y = 70 + (i * 59) % 300, r = 2.2 + (i % 3);
                var sp = Solid(Mix(sun, Colors.White, .6));
                Add(P($"M{F(x)} {F(y - r * 2)} L{F(x + r * .5)} {F(y - r * .5)} L{F(x + r * 2)} {F(y)} L{F(x + r * .5)} {F(y + r * .5)} L{F(x)} {F(y + r * 2)} L{F(x - r * .5)} {F(y + r * .5)} L{F(x - r * 2)} {F(y)} L{F(x - r * .5)} {F(y - r * .5)}Z", sp, .75));
            }
        }
        else if (t.Id == "light")
        {
            Orb(c, 620, 105, 38, Colors.White, Mix(sun, Colors.White, .3), 230, .9);
            Cloud(c, 330, 105, 1.2);
            Cloud(c, 470, 210, 1);
            Cloud(c, 190, 175, .9);
            Cloud(c, 680, 230, .8);
            Add(Ridge("M0 330 Q200 268 400 322 T800 298 V450 H0Z", Mix(m1, Colors.White, .25), m1));
            Add(Haze(280, 70, Colors.White, .5));
            Add(Ridge("M0 385 Q250 332 450 378 T800 350 V450 H0Z", Mix(m2, Colors.White, .2), m2));
            Add(Ridge("M0 435 Q300 388 520 428 T800 405 V450 H0Z", Mix(m3, Colors.White, .1), m3));
            foreach (var (bx, by) in new[] { (210.0, 120.0), (240.0, 140.0), (440.0, 90.0) })
                Add(P($"M{F(bx)} {F(by)} q6 -7 12 0 q6 -7 12 0", Solid(Colors.Transparent)).Also(sh => { sh.Stroke = Solid(Mix(m3, Colors.Black, .2)); sh.StrokeThickness = 1.6; }));
        }

        return new Viewbox { Stretch = Stretch.UniformToFill, Child = c };
    }

    static T Also<T>(this T value, Action<T> act)
    {
        act(value);
        return value;
    }

    // ---------- зерно и виньетка ----------

    static BitmapSource? noise;

    static BitmapSource Noise()
    {
        if (noise != null) return noise;
        const int n = 256;
        var px = new byte[n * n * 4];
        var rnd = new Random(7);
        for (int i = 0; i < n * n; i++)
        {
            byte v = (byte)rnd.Next(0, 256);
            byte a = (byte)rnd.Next(0, 16); // очень слабое зерно
            // предумноженный альфа-канал
            px[i * 4 + 0] = (byte)(v * a / 255);
            px[i * 4 + 1] = (byte)(v * a / 255);
            px[i * 4 + 2] = (byte)(v * a / 255);
            px[i * 4 + 3] = a;
        }
        var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, px, n * 4);
        bmp.Freeze();
        noise = bmp;
        return bmp;
    }

    /// <summary>Готовая картинка для обоев: пейзаж + виньетка + зерно (зерно рисуется в пикселях экрана, без масштабирования).</summary>
    public static UIElement BuildWallpaper(Theme t, int w, int h)
    {
        var grid = new Grid { Width = w, Height = h };
        grid.Children.Add(Build(t));

        var vignette = new RadialGradientBrush { Center = new Point(.5, .46), GradientOrigin = new Point(.5, .46), RadiusX = .78, RadiusY = .85 };
        vignette.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), .55));
        vignette.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0, 0, 0), 1));
        grid.Children.Add(new System.Windows.Shapes.Rectangle { Fill = vignette, IsHitTestVisible = false });

        var grain = new ImageBrush(Noise())
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 256, 256),
            Stretch = Stretch.None,
        };
        grid.Children.Add(new System.Windows.Shapes.Rectangle { Fill = grain, Opacity = t.Dark ? .9 : .6, IsHitTestVisible = false });
        return grid;
    }
}
