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
        public double Timer; // для салюта: время до следующего залпа
        public List<List<Point>> Bolts = new(); // ветви молнии
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

        // дополнительные эффекты поверх основных частиц: туман, лучи, круги на воде, боке
        var mist = new[] { "forest", "jungle", "mint", "midnight", "autumn", "volcano", "sepia", "winter", "graphite", "toxic", "storm", "lavender" };
        var rays = new[] { "ocean", "coral", "lagoon", "mint", "jungle", "gold", "lavender", "forest", "spring" };
        var ripple = new[] { "ocean", "lagoon", "coral", "steel", "storm", "midnight" };
        var bokeh = new[] { "cyber", "synthwave", "neon", "ruby", "crimson", "steel", "sunset", "candy", "scarlet" };
        if (Array.IndexOf(mist, t.Id) >= 0)
            layers.Add(new Layer { Mode = "mist", Colors = new[] { Soft(t.S3, .35), Soft(t.Accent, .5) }, SizeMin = 3, SizeMax = 6, VxMin = 6, VxMax = 18, Alpha = .5 });
        if (Array.IndexOf(rays, t.Id) >= 0)
            layers.Add(new Layer { Mode = "rays", Colors = new[] { Soft(t.Sun, .4) }, Alpha = 1 });
        if (Array.IndexOf(ripple, t.Id) >= 0)
            layers.Add(new Layer { Mode = "ripple", Colors = new[] { Soft(t.Accent, .5), white }, SizeMin = 1.2, SizeMax = 2.4, Alpha = .7 });
        if (Array.IndexOf(bokeh, t.Id) >= 0)
            layers.Add(new Layer { Mode = "bokeh", Colors = new[] { Col(t.Accent), Col(t.Accent2), Soft(t.Sun, .2) }, SizeMin = 3, SizeMax = 9, VxMin = -8, VxMax = 8, VyMin = -10, VyMax = -2, Alpha = .35 });

        // сложные анимации по темам
        string[] Ids(string list) => list.Split(',');
        var dark = Color.FromRgb((byte)(Col(t.M3).R * .45), (byte)(Col(t.M3).G * .45), (byte)(Col(t.M3).B * .5));
        if (Array.IndexOf(Ids("sunset,spring,autumn,lavender,desert,gold,rose,mint,light,ocean,lagoon,sakura,candy"), t.Id) >= 0)
            layers.Add(new Layer { Mode = "boids", Colors = new[] { t.Dark ? Soft(t.M1, .15) : dark }, Alpha = .85 });
        if (Array.IndexOf(Ids("ocean,lagoon,steel,midnight"), t.Id) >= 0)
            layers.Add(new Layer { Mode = "waves", Colors = new[] { Soft(t.Accent, .35), Soft(t.Accent2, .3), Soft(t.Sun, .2) }, Alpha = 1 });
        if (Array.IndexOf(Ids("cosmos,nebula"), t.Id) >= 0)
            layers.Add(new Layer { Mode = "galaxy", Colors = new[] { Soft(t.Accent, .45), Soft(t.Accent2, .45), white }, Alpha = .9 });
        if (Array.IndexOf(Ids("matrix,toxic"), t.Id) >= 0)
            layers.Add(new Layer { Mode = "digital", Colors = new[] { Col(t.Accent) }, Alpha = .9 });
        if (Array.IndexOf(Ids("candy,rose,gold,neon,sakura"), t.Id) >= 0)
            layers.Add(new Layer { Mode = "fireworks", Colors = new[] { Col(t.Accent), Col(t.Accent2), Soft(t.Sun, .2), white }, Alpha = .95 });
        if (Array.IndexOf(Ids("volcano,ruby,scarlet,crimson"), t.Id) >= 0)
            layers.Add(new Layer { Mode = "blobs", Colors = new[] { Col(t.Accent), Col(t.Accent2), Soft(t.Sun, .1) }, Alpha = .5 });
        if (Array.IndexOf(Ids("light,sakura,mint,lavender,ice,spring,candy,rose,sepia,winter"), t.Id) >= 0)
            layers.Add(new Layer { Mode = "clouds", Colors = new[] { t.Dark ? Soft(t.S3, .5) : white }, Alpha = t.Dark ? .22 : .5 });
        if (t.Id == "synthwave")
            layers.Add(new Layer { Mode = "grid", Colors = new[] { Col(t.Accent) }, Alpha = 1 });

        double w = Math.Max(ActualWidth, 800), h = Math.Max(ActualHeight, 450);
        foreach (var l in layers)
        {
            int count = l.Mode switch
            {
                "twinkle" => 18, "spark" => 14, "firefly" => 18, "rise" => 26, "fall" => 30, "petal" => 18, "snow" => 60,
                "rain" => 80, "shoot" => 2, "aurora" => 0, "lightning" => 1, "rays" => 0, "mist" => 5, "bokeh" => 14, "ripple" => 6,
                "boids" => 22, "galaxy" => 260, "digital" => 48, "fireworks" => 0, "blobs" => 7, "clouds" => 7, "waves" => 0, "grid" => 0, _ => 24,
            };
            for (int i = 0; i < count; i++)
            {
                var np = Spawn(l, w, h, true);
                InitSpecial(l, np, i, count, w, h);
                l.Items.Add(np);
            }
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
            if (l.Mode is "fireworks" or "boids" or "digital" or "clouds" or "blobs")
            {
                UpdateSpecial(l, now, dt, w, h);
                continue;
            }
            if (l.Mode is "waves" or "grid" or "galaxy") continue;

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
                    case "mist":
                        p.X += p.Vx * dt;
                        if (p.X > w + 400) p.X = -400;
                        break;
                    case "ripple":
                        p.Speed += dt;
                        if (p.Speed > 4) { p.X = rnd.NextDouble() * w; p.Y = h * (.45 + rnd.NextDouble() * .5); p.Speed = 0; }
                        break;
                    case "bokeh":
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
                            l.Bolts.Clear();
                            double bx = w * (.15 + rnd.NextDouble() * .7);
                            var main = Bolt(rnd, bx, 0, h * (.55 + rnd.NextDouble() * .2), w * .018, 16);
                            l.Bolts.Add(main);
                            for (int bi = 0; bi < 3; bi++)
                            {
                                var src = main[3 + rnd.Next(0, 10)];
                                var br = Bolt(rnd, src.X, src.Y, src.Y + h * (.12 + rnd.NextDouble() * .15), w * .012, 6);
                                for (int k2 = 0; k2 < br.Count; k2++) br[k2] = new Point(br[k2].X + (rnd.NextDouble() < .5 ? -1 : 1) * k2 * w * .006, br[k2].Y);
                                l.Bolts.Add(br);
                            }
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
        if (l.Flash > .3 && l.Bolts.Count > 0)
        {
            double o = Math.Min(1, (l.Flash - .3) * 2);
            var glow = RainPen(l.Colors[0], 9);
            var core = RainPen(Colors.White, 2.6);
            foreach (var b in l.Bolts)
            {
                for (int i = 0; i < b.Count - 1; i++)
                {
                    dc.PushOpacity(o * .4);
                    dc.DrawLine(glow, b[i], b[i + 1]);
                    dc.Pop();
                    dc.PushOpacity(o);
                    dc.DrawLine(core, b[i], b[i + 1]);
                    dc.Pop();
                }
            }
        }
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

    // ---------- сложные анимации: стаи птиц, волны, галактика, цифровой дождь, салют, лава, облака, молния, сетка ----------

    static readonly string RainChars = "01234567890ABCDEFXYZ<>/\\+=*#";
    static readonly Dictionary<(char, bool), FormattedText> GlyphCache = new();

    void InitSpecial(Layer l, Particle p, int i, int total, double w, double h)
    {
        switch (l.Mode)
        {
            case "boids":
                p.Y = h * (.1 + rnd.NextDouble() * .28);
                p.Vx = 45 + rnd.NextDouble() * 35;
                p.Vy = (rnd.NextDouble() - .5) * 20;
                p.Size = 1.2 + rnd.NextDouble() * 1.2;
                break;
            case "galaxy":
                p.Speed = Math.Pow(rnd.NextDouble(), .6);
                p.Phase = (i % 3) * 2.094 + (rnd.NextDouble() - .5) * .55;
                p.Size = .7 + rnd.NextDouble() * 1.7;
                break;
            case "digital":
                p.X = (i + .5) * w / total;
                p.Y = -rnd.NextDouble() * h;
                p.Vy = 110 + rnd.NextDouble() * 220;
                p.Phase = rnd.NextDouble() * 20;
                break;
            case "clouds":
                p.Y = h * (.04 + rnd.NextDouble() * .38);
                p.Size = 2 + rnd.NextDouble() * 3;
                p.Vx = 3 + p.Size * 2.2;
                break;
            case "blobs":
                p.Size = 4 + rnd.NextDouble() * 6;
                p.Vy = -(5 + rnd.NextDouble() * 14);
                break;
        }
    }

    void UpdateSpecial(Layer l, double now, double dt, double w, double h)
    {
        switch (l.Mode)
        {
            case "fireworks":
            {
                l.Timer -= dt;
                if (l.Timer <= 0)
                {
                    l.Timer = 1.2 + rnd.NextDouble() * 2.2;
                    double cx = w * (.15 + .7 * rnd.NextDouble()), cy = h * (.1 + .35 * rnd.NextDouble());
                    int col = rnd.Next(l.Colors.Length), n = 48;
                    for (int i = 0; i < n; i++)
                    {
                        double a = i * 6.2832 / n + rnd.NextDouble() * .1, sp = 110 + rnd.NextDouble() * 150;
                        l.Items.Add(new Particle { X = cx, Y = cy, Vx = Math.Cos(a) * sp, Vy = Math.Sin(a) * sp, Size = 1.5 + rnd.NextDouble() * 1.4, Phase = 0, Speed = 1.5 + rnd.NextDouble() * .8, Col = col });
                    }
                }
                for (int i = l.Items.Count - 1; i >= 0; i--)
                {
                    var p = l.Items[i];
                    p.Phase += dt;
                    p.Vx *= 1 - 1.3 * dt;
                    p.Vy = p.Vy * (1 - 1.3 * dt) + 95 * dt;
                    p.X += p.Vx * dt;
                    p.Y += p.Vy * dt;
                    if (p.Phase > p.Speed) l.Items.RemoveAt(i);
                }
                break;
            }
            case "boids":
                foreach (var p in l.Items)
                {
                    double cx = 0, cy = 0, ax = 0, ay = 0, sx = 0, sy = 0;
                    int n = 0;
                    foreach (var q in l.Items)
                    {
                        if (ReferenceEquals(p, q)) continue;
                        double dx = q.X - p.X, dy = q.Y - p.Y, d2 = dx * dx + dy * dy;
                        if (d2 > 45000) continue;
                        n++; cx += q.X; cy += q.Y; ax += q.Vx; ay += q.Vy;
                        if (d2 < 1800) { sx -= dx; sy -= dy; }
                    }
                    if (n > 0)
                    {
                        cx /= n; cy /= n; ax /= n; ay /= n;
                        p.Vx += ((cx - p.X) * .35 + (ax - p.Vx) * 1.4 + sx * 3) * dt;
                        p.Vy += ((cy - p.Y) * .35 + (ay - p.Vy) * 1.4 + sy * 3) * dt;
                    }
                    p.Vy += (h * .25 - p.Y) * .08 * dt + Math.Sin(now * .5 + p.Phase) * 8 * dt;
                    p.Vx += Math.Cos(now * .3 + p.Phase) * 6 * dt;
                    double sp = Math.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
                    if (sp > 105) { p.Vx *= 105 / sp; p.Vy *= 105 / sp; }
                    else if (sp < 40 && sp > .1) { p.Vx *= 40 / sp; p.Vy *= 40 / sp; }
                    p.X += p.Vx * dt;
                    p.Y += p.Vy * dt;
                    if (p.X > w + 40) p.X = -40; else if (p.X < -40) p.X = w + 40;
                }
                break;
            case "digital":
                foreach (var p in l.Items)
                {
                    p.Y += p.Vy * dt;
                    if (p.Y - 16 * 16 > h) { p.Y = -rnd.NextDouble() * 250; p.Vy = 110 + rnd.NextDouble() * 220; }
                }
                break;
            case "clouds":
                foreach (var p in l.Items)
                {
                    p.X += p.Vx * dt;
                    if (p.X > w + 320) p.X = -320;
                }
                break;
            case "blobs":
                foreach (var p in l.Items)
                {
                    p.Y += p.Vy * dt;
                    p.X += Math.Sin(now * .22 * p.Speed + p.Phase) * 16 * dt;
                    if (p.Y < -260) { p.Y = h + 260; p.X = rnd.NextDouble() * w; }
                }
                break;
        }
    }

    static List<Point> Bolt(Random r, double x, double y, double endY, double jitter, int segs)
    {
        var pts = new List<Point> { new(x, y) };
        for (int i = 1; i <= segs; i++)
        {
            x += (r.NextDouble() - .5) * jitter * 2;
            pts.Add(new Point(x, y + (endY - y) * i / segs));
        }
        return pts;
    }

    void DrawSpecial(DrawingContext dc, Layer l, double now, double w, double h)
    {
        var c0 = l.Colors[0];
        switch (l.Mode)
        {
            case "fireworks":
                foreach (var p in l.Items)
                {
                    double f = 1 - p.Phase / p.Speed;
                    var c = l.Colors[p.Col];
                    dc.PushOpacity(Math.Clamp(f * l.Alpha, 0, 1));
                    dc.DrawLine(RainPen(c, 1.2), new Point(p.X, p.Y), new Point(p.X - p.Vx * .05, p.Y - p.Vy * .05));
                    dc.DrawEllipse(GlowBrush(c), null, new Point(p.X, p.Y), p.Size * 5, p.Size * 5);
                    dc.DrawEllipse(CoreBrush(c), null, new Point(p.X, p.Y), p.Size * .8, p.Size * .8);
                    dc.Pop();
                }
                break;

            case "boids":
                foreach (var p in l.Items)
                {
                    double ang = Math.Atan2(p.Vy, p.Vx) * 180 / Math.PI;
                    double flap = .5 + .5 * Math.Sin(now * 7 * (.8 + p.Size * .15) + p.Phase * 9);
                    double s = p.Size * 5.5, wy = s * (.25 + .85 * flap);
                    dc.PushOpacity(l.Alpha);
                    dc.PushTransform(new TranslateTransform(p.X, p.Y));
                    dc.PushTransform(new RotateTransform(ang));
                    var pen = RainPen(c0, 1.7);
                    dc.DrawLine(pen, new Point(0, 0), new Point(-s, -wy));
                    dc.DrawLine(pen, new Point(0, 0), new Point(-s, wy));
                    dc.Pop(); dc.Pop(); dc.Pop();
                }
                break;

            case "waves":
                for (int r = 0; r < 3; r++)
                {
                    var c = l.Colors[r % l.Colors.Length];
                    double baseY = h * (.76 + r * .07), amp = h * (.012 + r * .006);
                    const int steps = 56;
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open())
                    {
                        for (int i = 0; i <= steps; i++)
                        {
                            double x = w * i / steps;
                            double y = baseY + Math.Sin(x * (.006 - r * .0012) + now * (.8 + r * .35) + r * 1.9) * amp + Math.Sin(x * .015 - now * .6 + r) * amp * .45;
                            if (i == 0) ctx.BeginFigure(new Point(x, y), true, true); else ctx.LineTo(new Point(x, y), true, false);
                        }
                        ctx.LineTo(new Point(w, h), true, false);
                        ctx.LineTo(new Point(0, h), true, false);
                    }
                    geo.Freeze();
                    var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(90 - r * 14), c.R, c.G, c.B), 0));
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
                    brush.Freeze();
                    dc.DrawGeometry(brush, new Pen(new SolidColorBrush(Color.FromArgb((byte)(110 - r * 25), 255, 255, 255)), 1.2), geo);
                }
                break;

            case "galaxy":
            {
                double cx = w * .7, cy = h * .3, R = Math.Min(w, h) * .4;
                dc.PushOpacity(.8);
                dc.DrawEllipse(GlowBrush(c0), null, new Point(cx, cy), R * .35, R * .16);
                dc.Pop();
                foreach (var p in l.Items)
                {
                    double r = p.Speed;
                    double ang = p.Phase + r * 4.4 + now * .05 / (.3 + r);
                    double x = cx + Math.Cos(ang) * r * R, y = cy + Math.Sin(ang) * r * R * .42;
                    var c = l.Colors[p.Col];
                    dc.PushOpacity(Math.Clamp(l.Alpha * (.35 + .65 * (1 - r)) * (.7 + .3 * Math.Sin(now * 2 + p.Phase * 5)), 0, 1));
                    dc.DrawEllipse(CoreBrush(c), null, new Point(x, y), p.Size, p.Size);
                    dc.DrawEllipse(GlowBrush(c), null, new Point(x, y), p.Size * 3.2, p.Size * 3.2);
                    dc.Pop();
                }
                break;
            }

            case "digital":
                foreach (var p in l.Items)
                {
                    for (int j = 0; j < 16; j++)
                    {
                        double y = p.Y - j * 16;
                        if (y < -16 || y > h) continue;
                        char ch = RainChars[((int)(now * 7 + p.Phase * 10 + j * 3.7)) % RainChars.Length];
                        bool head = j == 0;
                        if (!GlyphCache.TryGetValue((ch, head), out var ft))
                        {
                            var br = new SolidColorBrush(head ? Colors.White : c0);
                            br.Freeze();
                            ft = new FormattedText(ch.ToString(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 14, br, 1.0);
                            GlyphCache[(ch, head)] = ft;
                        }
                        dc.PushOpacity(head ? 1 : Math.Max(.05, (1 - j / 16.0) * .85) * l.Alpha);
                        dc.DrawText(ft, new Point(p.X, y));
                        dc.Pop();
                    }
                }
                break;

            case "blobs":
                foreach (var p in l.Items)
                {
                    var c = l.Colors[p.Col];
                    double r = p.Size * 22 * (1 + .12 * Math.Sin(now * .8 * p.Speed + p.Phase));
                    dc.PushOpacity(l.Alpha);
                    dc.DrawEllipse(GlowBrush(c), null, new Point(p.X, p.Y), r, r * 1.15);
                    dc.DrawEllipse(GlowBrush(c), null, new Point(p.X, p.Y), r * .55, r * .6);
                    dc.Pop();
                }
                break;

            case "clouds":
                foreach (var p in l.Items)
                {
                    double s = p.Size * 26;
                    dc.PushOpacity(l.Alpha);
                    dc.DrawEllipse(GlowBrush(c0), null, new Point(p.X, p.Y), s * 2.2, s * .8);
                    dc.DrawEllipse(GlowBrush(c0), null, new Point(p.X - s * 1.1, p.Y + s * .12), s * 1.5, s * .6);
                    dc.DrawEllipse(GlowBrush(c0), null, new Point(p.X + s * 1.2, p.Y + s * .1), s * 1.6, s * .65);
                    dc.DrawEllipse(GlowBrush(c0), null, new Point(p.X + s * .2, p.Y - s * .35), s * 1.2, s * .55);
                    dc.Pop();
                }
                break;

            case "grid":
            {
                double y0 = h * .667;
                var pen = RainPen(c0, 1.6);
                for (int i = 0; i < 12; i++)
                {
                    double t = (i + (now * .55) % 1) / 12.0;
                    double y = y0 + t * t * (h - y0);
                    dc.PushOpacity(.25 + .75 * t);
                    dc.DrawLine(RainPen(c0, 1 + t * 2.4), new Point(0, y), new Point(w, y));
                    dc.Pop();
                }
                dc.PushOpacity(.55);
                for (int k = -12; k <= 12; k++)
                    dc.DrawLine(pen, new Point(w / 2 + k * w * .012, y0), new Point(w / 2 + k * w * .12, h));
                dc.Pop();
                break;
            }
        }
    }

    void DrawRays(DrawingContext dc, Layer l, double now, double w, double h)
    {
        // косые столбы света сверху, медленно покачиваются и мерцают
        var c = l.Colors[0];
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(90, c.R, c.G, c.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        brush.Freeze();
        for (int i = 0; i < 5; i++)
        {
            double x0 = w * (.08 + i * .21) + Math.Sin(now * .18 + i * 1.7) * 50;
            double wid = 70 + 40 * Math.Sin(i * 2.3);
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(x0, 0), true, true);
                ctx.LineTo(new Point(x0 + wid, 0), true, false);
                ctx.LineTo(new Point(x0 + wid - h * .45, h * .8), true, false);
                ctx.LineTo(new Point(x0 - wid * .6 - h * .45, h * .8), true, false);
            }
            geo.Freeze();
            dc.PushOpacity(.25 + .35 * (.5 + .5 * Math.Sin(now * .35 + i * 1.3)));
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
            if (l.Mode == "rays") { DrawRays(dc, l, now, wAll, h); continue; }
            if (l.Mode is "fireworks" or "boids" or "digital" or "clouds" or "blobs" or "waves" or "grid" or "galaxy") { DrawSpecial(dc, l, now, wAll, h); continue; }

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
                if (l.Mode == "mist")
                {
                    dc.PushOpacity(a * (.6 + .4 * Math.Sin(now * .12 * p.Speed + p.Phase)));
                    dc.DrawEllipse(GlowBrush(color), null, new Point(p.X, p.Y), p.Size * 90, p.Size * 30);
                    dc.Pop();
                    continue;
                }
                if (l.Mode == "bokeh")
                {
                    dc.PushOpacity(a * (.4 + .6 * Math.Pow(.5 + .5 * Math.Sin(now * p.Speed * .5 + p.Phase), 2)));
                    dc.DrawEllipse(GlowBrush(color), null, new Point(p.X, p.Y), p.Size * 9, p.Size * 9);
                    dc.Pop();
                    continue;
                }
                if (l.Mode == "ripple")
                {
                    double k = p.Speed / 4;
                    dc.PushOpacity(Math.Clamp(a * (1 - k), 0, 1));
                    double rr = k * p.Size * 60;
                    dc.DrawEllipse(null, RainPen(color, 1.4), new Point(p.X, p.Y), rr, rr * .32);
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
