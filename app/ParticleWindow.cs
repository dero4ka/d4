using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace D4;

/// <summary>
/// Живые обои: прозрачное окно на весь экран под всеми окнами, по которому плывут частицы в духе текущей темы
/// (пузыри, угольки, светлячки, неоновые искры, блёстки). Мышь проходит сквозь окно.
/// </summary>
public sealed class ParticleWindow : Window
{
    readonly ParticleCanvas canvas = new();
    readonly DispatcherTimer guard = new() { Interval = TimeSpan.FromSeconds(1) };
    IntPtr hwnd;

    public ParticleWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "D4 Particles";
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        IsHitTestVisible = false;
        Content = canvas;

        Loaded += (_, _) =>
        {
            Native.SendToBottom(hwnd);
            guard.Start();
        };
        guard.Tick += (_, _) =>
        {
            try
            {
                Native.SendToBottom(hwnd);
                // под развёрнутым окном частицы не видны — не тратим процессор
                canvas.Paused = Native.IsForegroundCovering();
            }
            catch (Exception ex)
            {
                Settings.Log("particles guard: " + ex.Message);
            }
        };
        Closed += (_, _) =>
        {
            guard.Stop();
            canvas.Stop();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        hwnd = new WindowInteropHelper(this).Handle;
        // не в Alt+Tab, не берёт фокус, клики проходят сквозь (WS_EX_TRANSPARENT)
        Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | 0x20L);
    }
}

internal sealed class ParticleCanvas : FrameworkElement
{
    sealed class Particle
    {
        public double X, Y, Vx, Vy, Size, Phase, Speed;
        public int Col;
    }

    sealed class Layer
    {
        public string Mode = "drift";
        public Color[] Colors = Array.Empty<Color>();
        public double SizeMin = 1, SizeMax = 3, VxMin, VxMax, VyMin, VyMax, Glow = 4, Alpha = .8;
        public bool Flicker;
        public double Flash; // яркость вспышки молнии
        public readonly List<Particle> Items = new();
    }

    static readonly Dictionary<Color, Brush> GlowBrushes = new();
    static readonly Dictionary<Color, Brush> CoreBrushes = new();
    static Geometry? sparkle;

    readonly List<Layer> layers = new();
    readonly Random rnd = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    double last;
    bool running;
    public bool Paused { get; set; }
    /// <summary>Частота кадров анимации обоев (задаётся в настройках).</summary>
    public static int Fps { get; set; } = 24;

    public ParticleCanvas()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            Configure(ThemeManager.Current);
            ThemeManager.Changed += OnTheme;
            CompositionTarget.Rendering += OnFrame;
            running = true;
        };
        Unloaded += (_, _) => Stop();
    }

    void OnTheme() => Configure(ThemeManager.Current);

    public void Stop()
    {
        if (!running) return;
        running = false;
        CompositionTarget.Rendering -= OnFrame;
        ThemeManager.Changed -= OnTheme;
    }

    static Color Col(string hex) => ThemeManager.C(hex);

    static Color Soft(string hex, double toWhite)
    {
        var c = Col(hex);
        return Color.FromRgb((byte)(c.R + (255 - c.R) * toWhite), (byte)(c.G + (255 - c.G) * toWhite), (byte)(c.B + (255 - c.B) * toWhite));
    }

    // ---------- наборы частиц по темам ----------

    void Configure(Theme t)
    {
        layers.Clear();
        var white = Colors.White;

        switch (t.Id)
        {
            case "ocean":
                layers.Add(new Layer { Mode = "rise", Colors = new[] { Soft(t.Sun, .2), white, Col(t.Accent) }, SizeMin = 1.5, SizeMax = 3.8, VyMin = -12, VyMax = -30, VxMin = -4, VxMax = 4, Glow = 3.5, Alpha = .55 });
                layers.Add(new Layer { Mode = "twinkle", Colors = new[] { white, Soft(t.Accent, .5) }, SizeMin = 1, SizeMax = 2, Glow = 4, Alpha = .9 });
                break;
            case "sunset":
                layers.Add(new Layer { Mode = "drift", Colors = new[] { Col(t.Sun), Soft(t.S3, .3), Col(t.Accent) }, SizeMin = 1.5, SizeMax = 3.5, VxMin = 10, VxMax = 30, VyMin = -14, VyMax = -3, Glow = 4.5, Alpha = .7 });
                break;
            case "forest":
                layers.Add(new Layer { Mode = "firefly", Colors = new[] { Col(t.Accent2), Col(t.Sun) }, SizeMin = 1.8, SizeMax = 3.2, Glow = 6.5, Alpha = 1 });
                layers.Add(new Layer { Mode = "drift", Colors = new[] { Col(t.Sun) }, SizeMin = 1, SizeMax = 2, VxMin = 3, VxMax = 12, VyMin = 2, VyMax = 8, Glow = 3, Alpha = .35 });
                break;
            case "neon":
                layers.Add(new Layer { Mode = "rise", Colors = new[] { Col(t.Accent), Col(t.Accent2), white }, SizeMin = 1.5, SizeMax = 3.2, VyMin = -28, VyMax = -75, VxMin = -3, VxMax = 3, Glow = 5.5, Alpha = .85 });
                layers.Add(new Layer { Mode = "twinkle", Colors = new[] { Col(t.Accent2), white }, SizeMin = 1, SizeMax = 2, Glow = 5, Alpha = .9 });
                break;
            case "scarlet":
                layers.Add(new Layer { Mode = "rise", Colors = new[] { Col(t.Accent2), Col(t.Accent), Col(t.Sun) }, SizeMin = 1.2, SizeMax = 3.4, VyMin = -35, VyMax = -95, VxMin = -8, VxMax = 24, Glow = 5, Alpha = .9, Flicker = true });
                break;
            case "crimson":
                layers.Add(new Layer { Mode = "fall", Colors = new[] { Col(t.Accent), Soft(t.Accent2, .3), Soft(t.Muted, 0) }, SizeMin = 1, SizeMax = 2.6, VyMin = 8, VyMax = 26, VxMin = -7, VxMax = 7, Glow = 3, Alpha = .55 });
                layers.Add(new Layer { Mode = "twinkle", Colors = new[] { white, Soft(t.Accent2, .6) }, SizeMin = 1, SizeMax = 1.8, Glow = 4, Alpha = .9 });
                break;
            case "aurora":
                layers.Add(new Layer { Mode = "aurora", Colors = new[] { Col(t.Accent), Col(t.Accent2), Soft(t.Accent, .2) }, Alpha = 1 });
                layers.Add(new Layer { Mode = "twinkle", Colors = new[] { white, Soft(t.Accent, .6) }, SizeMin = 1, SizeMax = 2, Glow = 4, Alpha = .9 });
                layers.Add(new Layer { Mode = "shoot", Colors = new[] { white }, Alpha = 1 });
                break;
            case "sakura":
                layers.Add(new Layer { Mode = "petal", Colors = new[] { Col(t.Accent2), Soft(t.Accent, .35), white }, SizeMin = 4, SizeMax = 9, VyMin = 18, VyMax = 46, VxMin = 6, VxMax = 30, Alpha = .9 });
                break;
            case "cosmos":
                layers.Add(new Layer { Mode = "twinkle", Colors = new[] { white, Soft(t.Accent, .5), Soft(t.Accent2, .5) }, SizeMin = 1, SizeMax = 2.4, Glow = 4, Alpha = 1 });
                layers.Add(new Layer { Mode = "shoot", Colors = new[] { white }, Alpha = 1 });
                layers.Add(new Layer { Mode = "drift", Colors = new[] { Soft(t.Accent, .3), Col(t.Accent2) }, SizeMin = .8, SizeMax = 1.8, VxMin = 3, VxMax = 10, VyMin = -3, VyMax = 3, Glow = 3, Alpha = .5 });
                break;
            case "winter":
                layers.Add(new Layer { Mode = "snow", Colors = new[] { white, Soft(t.Accent, .7) }, SizeMin = 1.2, SizeMax = 4.6, VyMin = 14, VyMax = 46, VxMin = -8, VxMax = 8, Glow = 2.2, Alpha = .9 });
                break;
            case "storm":
                layers.Add(new Layer { Mode = "rain", Colors = new[] { Soft(t.Accent, .55) }, SizeMin = 1, SizeMax = 1.6, VyMin = 520, VyMax = 820, VxMin = -90, VxMax = -60, Alpha = .38 });
                layers.Add(new Layer { Mode = "lightning", Colors = new[] { Soft(t.Accent, .8) }, Alpha = 1 });
                break;
            case "ruby":
                layers.Add(new Layer { Mode = "spark", Colors = new[] { Col(t.Sun), white, Soft(t.Accent, .4) }, SizeMin = 3, SizeMax = 7, Glow = 2.2, Alpha = .95 });
                layers.Add(new Layer { Mode = "drift", Colors = new[] { Soft(t.Accent, .3), Col(t.Accent2) }, SizeMin = 1, SizeMax = 2.4, VxMin = 4, VxMax = 14, VyMin = -8, VyMax = -2, Glow = 3.5, Alpha = .5 });
                break;
            case "synthwave":
                layers.Add(new Layer { Mode = "twinkle", Colors = new[] { white, Soft(t.Accent, .5), Soft(t.Accent2, .5) }, SizeMin = 1, SizeMax = 2.4, Glow = 4.5, Alpha = 1 });
                layers.Add(new Layer { Mode = "drift", Colors = new[] { Col(t.Accent2), Soft(t.Accent, .4) }, SizeMin = 1, SizeMax = 2, VxMin = 3, VxMax = 10, VyMin = -6, VyMax = 0, Glow = 3, Alpha = .5 });
                break;
            case "desert":
                layers.Add(new Layer { Mode = "drift", Colors = new[] { Col(t.Sun), Soft(t.Accent, .35), Col(t.Accent2) }, SizeMin = 1.5, SizeMax = 3.6, VxMin = 8, VxMax = 26, VyMin = -8, VyMax = 6, Glow = 3.5, Alpha = .65 });
                break;
            case "midnight":
                layers.Add(new Layer { Mode = "twinkle", Colors = new[] { white, Soft(t.Accent, .5) }, SizeMin = 1, SizeMax = 2.2, Glow = 4, Alpha = 1 });
                layers.Add(new Layer { Mode = "shoot", Colors = new[] { white }, Alpha = 1 });
                break;
            case "mint":
                layers.Add(new Layer { Mode = "drift", Colors = new[] { Col(t.Sun), Soft(t.Accent, .35), Col(t.Accent2) }, SizeMin = 1.5, SizeMax = 3.6, VxMin = 8, VxMax = 26, VyMin = -8, VyMax = 6, Glow = 3.5, Alpha = .65 });
                break;
            case "lavender":
                layers.Add(new Layer { Mode = "petal", Colors = new[] { Col(t.Accent), Col(t.Accent2), Soft(t.Accent, .5) }, SizeMin = 4, SizeMax = 8, VyMin = 16, VyMax = 40, VxMin = 6, VxMax = 26, Alpha = .85 });
                break;
            case "gold":
                layers.Add(new Layer { Mode = "spark", Colors = new[] { Col(t.Sun), Col(t.Accent), Col(t.Accent2) }, SizeMin = 3, SizeMax = 7, Glow = 2.4, Alpha = .95 });
                layers.Add(new Layer { Mode = "drift", Colors = new[] { Soft(t.Accent, .3) }, SizeMin = 1, SizeMax = 2.2, VxMin = 3, VxMax = 12, VyMin = -8, VyMax = -2, Glow = 3, Alpha = .5 });
                break;
            case "ice":
                layers.Add(new Layer { Mode = "snow", Colors = new[] { white, Soft(t.Accent, .6) }, SizeMin = 1.2, SizeMax = 4.4, VyMin = 14, VyMax = 44, VxMin = -8, VxMax = 8, Glow = 2.2, Alpha = .9 });
                break;
            case "volcano":
                layers.Add(new Layer { Mode = "spark", Colors = new[] { Col(t.Sun), Col(t.Accent), Col(t.Accent2) }, SizeMin = 3, SizeMax = 7, Glow = 2.4, Alpha = .95 });
                layers.Add(new Layer { Mode = "drift", Colors = new[] { Soft(t.Accent, .3) }, SizeMin = 1, SizeMax = 2.2, VxMin = 3, VxMax = 12, VyMin = -8, VyMax = -2, Glow = 3, Alpha = .5 });
                break;
            case "toxic":
                layers.Add(new Layer { Mode = "rise", Colors = new[] { Col(t.Accent), Col(t.Accent2), white }, SizeMin = 1.5, SizeMax = 3.8, VyMin = -14, VyMax = -50, VxMin = -4, VxMax = 4, Glow = 4, Alpha = .7 });
                break;
            case "cyber":
                layers.Add(new Layer { Mode = "rain", Colors = new[] { Soft(t.Accent, .4) }, SizeMin = 1, SizeMax = 1.6, VyMin = 520, VyMax = 820, VxMin = -90, VxMax = -60, Alpha = .4 });
                layers.Add(new Layer { Mode = "twinkle", Colors = new[] { Col(t.Accent2), white }, SizeMin = 1, SizeMax = 1.8, Glow = 4, Alpha = .8 });
                break;
            case "coral":
                layers.Add(new Layer { Mode = "rise", Colors = new[] { Col(t.Accent), Col(t.Accent2), white }, SizeMin = 1.5, SizeMax = 3.8, VyMin = -14, VyMax = -50, VxMin = -4, VxMax = 4, Glow = 4, Alpha = .7 });
                break;
            case "lagoon":
                layers.Add(new Layer { Mode = "rise", Colors = new[] { Col(t.Accent), Col(t.Accent2), white }, SizeMin = 1.5, SizeMax = 3.8, VyMin = -14, VyMax = -50, VxMin = -4, VxMax = 4, Glow = 4, Alpha = .7 });
                break;
            case "autumn":
                layers.Add(new Layer { Mode = "fall", Colors = new[] { Col(t.Accent), Col(t.Accent2), Soft(t.Accent, .3) }, SizeMin = 3, SizeMax = 7, VyMin = 14, VyMax = 38, VxMin = -10, VxMax = 14, Glow = 0, Alpha = .85 });
                break;
            case "spring":
                layers.Add(new Layer { Mode = "petal", Colors = new[] { Col(t.Accent), Col(t.Accent2), Soft(t.Accent, .5) }, SizeMin = 4, SizeMax = 8, VyMin = 16, VyMax = 40, VxMin = 6, VxMax = 26, Alpha = .85 });
                break;
            case "graphite":
                layers.Add(new Layer { Mode = "drift", Colors = new[] { Col(t.Sun), Soft(t.Accent, .35), Col(t.Accent2) }, SizeMin = 1.5, SizeMax = 3.6, VxMin = 8, VxMax = 26, VyMin = -8, VyMax = 6, Glow = 3.5, Alpha = .65 });
                break;
            case "nebula":
                layers.Add(new Layer { Mode = "twinkle", Colors = new[] { white, Soft(t.Accent, .5) }, SizeMin = 1, SizeMax = 2.2, Glow = 4, Alpha = 1 });
                layers.Add(new Layer { Mode = "shoot", Colors = new[] { white }, Alpha = 1 });
                break;
            case "candy":
                layers.Add(new Layer { Mode = "petal", Colors = new[] { Col(t.Accent), Col(t.Accent2), Soft(t.Accent, .5) }, SizeMin = 4, SizeMax = 8, VyMin = 16, VyMax = 40, VxMin = 6, VxMax = 26, Alpha = .85 });
                break;
            case "matrix":
                layers.Add(new Layer { Mode = "rain", Colors = new[] { Soft(t.Accent, .4) }, SizeMin = 1, SizeMax = 1.6, VyMin = 520, VyMax = 820, VxMin = -90, VxMax = -60, Alpha = .4 });
                layers.Add(new Layer { Mode = "twinkle", Colors = new[] { Col(t.Accent2), white }, SizeMin = 1, SizeMax = 1.8, Glow = 4, Alpha = .8 });
                break;
            case "sepia":
                layers.Add(new Layer { Mode = "drift", Colors = new[] { Col(t.Sun), Soft(t.Accent, .35), Col(t.Accent2) }, SizeMin = 1.5, SizeMax = 3.6, VxMin = 8, VxMax = 26, VyMin = -8, VyMax = 6, Glow = 3.5, Alpha = .65 });
                break;
            case "rose":
                layers.Add(new Layer { Mode = "petal", Colors = new[] { Col(t.Accent), Col(t.Accent2), Soft(t.Accent, .5) }, SizeMin = 4, SizeMax = 8, VyMin = 16, VyMax = 40, VxMin = 6, VxMax = 26, Alpha = .85 });
                break;
            case "jungle":
                layers.Add(new Layer { Mode = "firefly", Colors = new[] { Col(t.Accent2), Col(t.Sun) }, SizeMin = 1.8, SizeMax = 3.2, Glow = 6.5, Alpha = 1 });
                layers.Add(new Layer { Mode = "drift", Colors = new[] { Col(t.Sun) }, SizeMin = 1, SizeMax = 2, VxMin = 3, VxMax = 12, VyMin = 2, VyMax = 8, Glow = 3, Alpha = .35 });
                break;
            case "steel":
                layers.Add(new Layer { Mode = "rain", Colors = new[] { Soft(t.Accent, .4) }, SizeMin = 1, SizeMax = 1.6, VyMin = 520, VyMax = 820, VxMin = -90, VxMax = -60, Alpha = .4 });
                layers.Add(new Layer { Mode = "twinkle", Colors = new[] { Col(t.Accent2), white }, SizeMin = 1, SizeMax = 1.8, Glow = 4, Alpha = .8 });
                break;
            default: // светлая: пух и пыльца
                layers.Add(new Layer { Mode = "drift", Colors = new[] { white, Soft(t.Accent, .6), Soft(t.Sun, .2) }, SizeMin = 2, SizeMax = 4.6, VxMin = 8, VxMax = 22, VyMin = 3, VyMax = 12, Glow = 3, Alpha = .75 });
                break;
        }

        double w = Math.Max(ActualWidth, 800), h = Math.Max(ActualHeight, 450);
        foreach (var l in layers)
        {
            int count = l.Mode switch
            {
                "twinkle" => 18, "spark" => 14, "firefly" => 18, "rise" => 26, "fall" => 30, "petal" => 18, "snow" => 60,
                "rain" => 80, "shoot" => 2, "aurora" => 0, "lightning" => 1, _ => 24,
            };
            for (int i = 0; i < count; i++) l.Items.Add(Spawn(l, w, h, true));
        }
    }

    Particle Spawn(Layer l, double w, double h, bool anywhere)
    {
        var p = new Particle
        {
            X = rnd.NextDouble() * w,
            Y = rnd.NextDouble() * h,
            Size = l.SizeMin + rnd.NextDouble() * (l.SizeMax - l.SizeMin),
            Phase = rnd.NextDouble() * Math.PI * 2,
            Speed = .6 + rnd.NextDouble() * 1.6,
            Col = rnd.Next(l.Colors.Length),
            Vx = l.VxMin + rnd.NextDouble() * (l.VxMax - l.VxMin),
            Vy = l.VyMin + rnd.NextDouble() * (l.VyMax - l.VyMin),
        };
        if (l.Mode == "shoot") { p.Speed = 0; p.Phase = 1 + rnd.NextDouble() * 5; }
        if (l.Mode == "lightning") p.Phase = 3 + rnd.NextDouble() * 5;
        if (!anywhere)
        {
            if (l.Mode == "rise") p.Y = h + 10;
            else if (l.Mode == "fall" || l.Mode == "snow" || l.Mode == "petal" || l.Mode == "rain") p.Y = -10;
            else if (l.Mode == "drift")
            {
                if (l.VxMax > 0) p.X = -10;
                else p.X = w + 10;
            }
        }
        return p;
    }

    // ---------- кадры ----------

    void OnFrame(object? sender, EventArgs e)
    {
        if (Paused) return;
        double now = clock.Elapsed.TotalSeconds;
        double dt = now - last;
        if (dt < 1.0 / Math.Clamp(Fps, 5, 120)) return;
        last = now;
        dt = Math.Min(dt, .15);

        double w = ActualWidth, h = ActualHeight;
        if (w < 10 || h < 10) return;

        foreach (var l in layers)
        {
            foreach (var p in l.Items)
            {
                switch (l.Mode)
                {
                    case "rise":
                        p.Y += p.Vy * dt;
                        p.X += (p.Vx + Math.Sin(now * p.Speed + p.Phase) * 9) * dt;
                        if (p.Y < -12) Respawn(l, p, w, h);
                        break;
                    case "fall":
                        p.Y += p.Vy * dt;
                        p.X += (p.Vx + Math.Sin(now * p.Speed * .7 + p.Phase) * 6) * dt;
                        if (p.Y > h + 12) Respawn(l, p, w, h);
                        break;
                    case "drift":
                        p.X += p.Vx * dt;
                        p.Y += (p.Vy + Math.Sin(now * p.Speed + p.Phase) * 5) * dt;
                        if (p.X > w + 12 || p.X < -12 || p.Y > h + 12 || p.Y < -12) Respawn(l, p, w, h);
                        break;
                    case "snow":
                        p.Y += p.Vy * dt * (.5 + p.Size / 4.6);
                        p.X += (p.Vx + Math.Sin(now * p.Speed * .8 + p.Phase) * 14) * dt;
                        if (p.Y > h + 12 || p.X < -20 || p.X > w + 20) Respawn(l, p, w, h);
                        break;
                    case "petal":
                        p.Y += p.Vy * dt;
                        p.X += (p.Vx + Math.Sin(now * p.Speed + p.Phase) * 22) * dt;
                        if (p.Y > h + 14 || p.X > w + 20) Respawn(l, p, w, h);
                        break;
                    case "rain":
                        p.Y += p.Vy * dt;
                        p.X += p.Vx * dt;
                        if (p.Y > h + 20 || p.X < -40) Respawn(l, p, w, h);
                        break;
                    case "shoot":
                        if (p.Speed > 0)
                        {
                            // метеор летит: Speed — сколько ещё живёт
                            p.X += p.Vx * dt;
                            p.Y += p.Vy * dt;
                            p.Speed -= dt;
                            if (p.Speed <= 0) p.Phase = 2.5 + rnd.NextDouble() * 6; // пауза до следующего
                        }
                        else
                        {
                            p.Phase -= dt;
                            if (p.Phase <= 0)
                            {
                                p.X = w * (.35 + rnd.NextDouble() * .6);
                                p.Y = h * rnd.NextDouble() * .35;
                                p.Vx = -520 - rnd.NextDouble() * 200;
                                p.Vy = 240 + rnd.NextDouble() * 160;
                                p.Speed = 1.1;
                            }
                        }
                        break;
                    case "lightning":
                        l.Flash = Math.Max(0, l.Flash - dt * 2.4);
                        p.Phase -= dt;
                        if (p.Phase <= 0)
                        {
                            l.Flash = 1;
                            p.Phase = 4 + rnd.NextDouble() * 9;
                        }
                        break;
                    case "firefly":
                        p.X += Math.Cos(now * .45 * p.Speed + p.Phase) * 22 * dt;
                        p.Y += Math.Sin(now * .33 * p.Speed + p.Phase * 1.3) * 16 * dt;
                        p.X = (p.X + w) % w;
                        p.Y = Math.Clamp(p.Y, 0, h);
                        break;
                }
            }
        }
        InvalidateVisual();
    }

    void Respawn(Layer l, Particle p, double w, double h)
    {
        var n = Spawn(l, w, h, false);
        p.X = n.X; p.Y = n.Y; p.Vx = n.Vx; p.Vy = n.Vy; p.Size = n.Size; p.Col = n.Col; p.Phase = n.Phase; p.Speed = n.Speed;
    }

    static Brush GlowBrush(Color c)
    {
        if (GlowBrushes.TryGetValue(c, out var b)) return b;
        var g = new RadialGradientBrush();
        g.GradientStops.Add(new GradientStop(Color.FromArgb(200, c.R, c.G, c.B), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(60, c.R, c.G, c.B), .4));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        g.Freeze();
        GlowBrushes[c] = g;
        return g;
    }

    static Brush CoreBrush(Color c)
    {
        if (CoreBrushes.TryGetValue(c, out var b)) return b;
        var core = new SolidColorBrush(Color.FromRgb((byte)(c.R + (255 - c.R) * .55), (byte)(c.G + (255 - c.G) * .55), (byte)(c.B + (255 - c.B) * .55)));
        core.Freeze();
        CoreBrushes[c] = core;
        return core;
    }

    static Geometry Sparkle()
    {
        if (sparkle != null) return sparkle;
        // четырёхлучевая звёздочка единичного размера
        var g = Geometry.Parse("M0,-1 L0.18,-0.18 L1,0 L0.18,0.18 L0,1 L-0.18,0.18 L-1,0 L-0.18,-0.18 Z");
        g.Freeze();
        sparkle = g;
        return g;
    }

    static Geometry? petal;

    static Geometry Petal()
    {
        if (petal != null) return petal;
        // лепесток единичного размера: заострённая капля
        var g = Geometry.Parse("M0,-1 C0.9,-0.6 0.9,0.5 0,1 C-0.9,0.5 -0.9,-0.6 0,-1 Z");
        g.Freeze();
        petal = g;
        return g;
    }

    static readonly Dictionary<(Color, int), Pen> RainPens = new();

    static Pen RainPen(Color c, double size)
    {
        var key = (c, (int)(size * 10));
        if (RainPens.TryGetValue(key, out var p)) return p;
        p = new Pen(new SolidColorBrush(c), size) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        p.Freeze();
        RainPens[key] = p;
        return p;
    }

    static void DrawMeteor(DrawingContext dc, Particle p)
    {
        double life = Math.Min(1, p.Speed / 0.4) * Math.Min(1, (1.1 - p.Speed) / 0.1 + .3);
        double len = Math.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
        double ux = p.Vx / len, uy = p.Vy / len;
        for (int i = 0; i < 16; i++)
        {
            double k = i / 15.0;
            double x = p.X - ux * k * 150, y = p.Y - uy * k * 150;
            double r = 2.2 * (1 - k) + .4;
            dc.PushOpacity(Math.Clamp(life * (1 - k) * .9, 0, 1));
            dc.DrawEllipse(Brushes.White, null, new Point(x, y), r, r);
            dc.Pop();
        }
    }

    void DrawFlash(DrawingContext dc, Layer l, double w, double h)
    {
        if (l.Flash < .02) return;
        // две быстрые вспышки подряд
        double f = l.Flash * (.55 + .45 * Math.Abs(Math.Sin(l.Flash * 18)));
        dc.PushOpacity(Math.Min(.5, f * .5));
        dc.DrawRectangle(new SolidColorBrush(l.Colors[0]), null, new Rect(0, 0, w, h));
        dc.Pop();
    }

    void DrawAurora(DrawingContext dc, Layer l, double now, double w, double h)
    {
        // три плавные ленты северного сияния, колеблются и переливаются
        for (int r = 0; r < 3; r++)
        {
            var c = l.Colors[r % l.Colors.Length];
            double baseY = h * (.18 + r * .07);
            double amp = h * (.05 + r * .015);
            double thick = h * (.2 + r * .04);
            const int steps = 40;

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (int i = 0; i <= steps; i++)
                {
                    double x = w * i / steps;
                    double y = baseY + Math.Sin(x * .004 + now * (.35 + r * .12) + r * 2) * amp + Math.Sin(x * .011 - now * .22 + r) * amp * .5;
                    if (i == 0) ctx.BeginFigure(new Point(x, y), true, true);
                    else ctx.LineTo(new Point(x, y), true, false);
                }
                for (int i = steps; i >= 0; i--)
                {
                    double x = w * i / steps;
                    double y = baseY + Math.Sin(x * .004 + now * (.35 + r * .12) + r * 2) * amp + Math.Sin(x * .011 - now * .22 + r) * amp * .5
                               + thick * (.7 + .3 * Math.Sin(x * .006 + now * .4 + r));
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
            geo.Freeze();

            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(120, c.R, c.G, c.B), .35));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            brush.Freeze();

            dc.PushOpacity(.55 + .35 * Math.Sin(now * .5 + r * 1.7));
            dc.DrawGeometry(brush, null, geo);
            dc.Pop();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double now = clock.Elapsed.TotalSeconds;
        double h = ActualHeight;

        double wAll = ActualWidth;
        foreach (var l in layers)
        {
            if (l.Mode == "aurora") { DrawAurora(dc, l, now, wAll, h); continue; }
            if (l.Mode == "lightning") { DrawFlash(dc, l, wAll, h); continue; }

            foreach (var p in l.Items)
            {
                double a = l.Alpha;
                switch (l.Mode)
                {
                    case "twinkle":
                    case "spark":
                        a *= .15 + .85 * Math.Pow(.5 + .5 * Math.Sin(now * p.Speed * 1.4 + p.Phase), 2);
                        break;
                    case "firefly":
                        a *= Math.Pow(.5 + .5 * Math.Sin(now * p.Speed * 1.1 + p.Phase), 3);
                        break;
                    case "rise":
                        a *= Math.Min(1, Math.Max(0, p.Y) / 90) * Math.Min(1, (h - p.Y + 20) / 60);
                        break;
                }
                if (l.Flicker) a *= .65 + .35 * Math.Sin(now * p.Speed * 6 + p.Phase);
                if (a < .02) continue;

                var color = l.Colors[p.Col];

                if (l.Mode == "shoot")
                {
                    if (p.Speed > 0) DrawMeteor(dc, p);
                    continue;
                }
                if (l.Mode == "rain")
                {
                    var pen = RainPen(color, p.Size);
                    dc.PushOpacity(Math.Min(1, a));
                    dc.DrawLine(pen, new Point(p.X, p.Y), new Point(p.X - p.Vx * .022, p.Y - p.Vy * .022));
                    dc.Pop();
                    continue;
                }
                if (l.Mode == "petal")
                {
                    dc.PushOpacity(Math.Min(1, a));
                    dc.PushTransform(new TranslateTransform(p.X, p.Y));
                    dc.PushTransform(new RotateTransform(now * 40 * p.Speed + p.Phase * 57));
                    dc.PushTransform(new ScaleTransform(p.Size, p.Size * (.55 + .45 * Math.Abs(Math.Sin(now * p.Speed + p.Phase)))));
                    dc.DrawGeometry(CoreBrush(color), null, Petal());
                    dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
                    continue;
                }

                dc.PushOpacity(Math.Min(1, a));
                if (l.Mode == "spark")
                {
                    dc.PushTransform(new TranslateTransform(p.X, p.Y));
                    dc.PushTransform(new RotateTransform(Math.Sin(now * .3 + p.Phase) * 25));
                    dc.PushTransform(new ScaleTransform(p.Size * 1.4, p.Size * 1.4));
                    dc.DrawGeometry(CoreBrush(color), null, Sparkle());
                    dc.Pop(); dc.Pop(); dc.Pop();
                    double gr = p.Size * l.Glow * 2;
                    dc.DrawEllipse(GlowBrush(color), null, new Point(p.X, p.Y), gr, gr);
                }
                else
                {
                    double gr = p.Size * l.Glow;
                    dc.DrawEllipse(GlowBrush(color), null, new Point(p.X, p.Y), gr, gr);
                    dc.DrawEllipse(CoreBrush(color), null, new Point(p.X, p.Y), p.Size * .7, p.Size * .7);
                }
                dc.Pop();
            }
        }
    }
}
