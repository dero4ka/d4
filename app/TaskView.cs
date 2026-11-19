using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace D4;

/// <summary>Обзор окон в стиле темы D4 (вместо системного Win+Tab).</summary>
public static class TaskView
{
    static TaskViewWindow? window;
    static DateTime closedAt = DateTime.MinValue;

    public static void Toggle()
    {
        if (window != null)
        {
            window.Close();
            return;
        }
        if ((DateTime.UtcNow - closedAt).TotalMilliseconds < 350) return;

        try
        {
            var w = new TaskViewWindow();
            window = w;
            w.Closed += (_, _) =>
            {
                window = null;
                closedAt = DateTime.UtcNow;
            };
            w.Show();
        }
        catch (Exception ex)
        {
            window = null;
            Settings.Log("taskview: " + ex);
        }
    }
}

internal sealed class TaskViewWindow : Window
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    struct DWM_THUMBNAIL_PROPERTIES
    {
        public uint dwFlags;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES props);
    [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);

    sealed class Card
    {
        public WinInfo Win = null!;
        public FrameworkElement Preview = null!;
        public IntPtr Thumb;
    }

    readonly Grid root = new();
    readonly WrapPanel wrap = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock empty = Ui.Text("Нет открытых окон", 18, "MutedBrush");
    readonly List<Card> cards = new();
    IntPtr hwnd;

    public TaskViewWindow()
    {
        // без прозрачности: иначе системные миниатюры окон (DWM) не рисуются
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Title = "D4 Task View";
        this.Dyn(BackgroundProperty, "BgBrush");

        root.Background = Brushes.Transparent;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var title = Ui.Text("Открытые окна", 24, "TextBrush", true);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, 48, 0, 0);
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        Grid.SetRow(wrap, 1);
        root.Children.Add(wrap);

        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.VerticalAlignment = VerticalAlignment.Center;
        empty.Visibility = Visibility.Collapsed;
        Grid.SetRow(empty, 1);
        root.Children.Add(empty);

        root.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource == root || e.OriginalSource == wrap) Close();
        };
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        Deactivated += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsActive) Close();
        }));
        SourceInitialized += (_, _) => hwnd = new WindowInteropHelper(this).Handle;
        Loaded += (_, _) =>
        {
            Native.ForceForeground(hwnd);
            Build();
            root.Opacity = 0;
            root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        };
        Closed += (_, _) => DropThumbs();
    }

    void DropThumbs()
    {
        foreach (var c in cards)
        {
            if (c.Thumb != IntPtr.Zero) DwmUnregisterThumbnail(c.Thumb);
            c.Thumb = IntPtr.Zero;
        }
        cards.Clear();
    }

    void Build()
    {
        DropThumbs();
        wrap.Children.Clear();

        var wins = Native.GetTaskWindows().Take(20).ToList();
        empty.Visibility = wins.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (wins.Count == 0) return;

        int cols = (int)Math.Ceiling(Math.Sqrt(wins.Count * 1.7));
        int rowsCount = (int)Math.Ceiling(wins.Count / (double)cols);
        double cardW = Math.Min(340, (Width - 160) / cols - 20);
        double cardH = Math.Min(cardW * 0.78, (Height - 260) / rowsCount - 20);
        cardW = Math.Max(160, cardW);
        cardH = Math.Max(120, cardH);
        wrap.MaxWidth = cols * (cardW + 20) + 4;

        foreach (var w in wins)
        {
            var card = new Card { Win = w };
            wrap.Children.Add(MakeCard(card, cardW, cardH));
            cards.Add(card);
        }

        // миниатюры привязываем к уже посчитанной раскладке
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PlaceThumbs));
    }

    FrameworkElement MakeCard(Card c, double w, double h)
    {
        var w0 = c.Win;
        var iconSrc = (w0.ExePath != null ? AppUtil.GetIcon(w0.ExePath) : null) ?? AppUtil.GetWindowIcon(w0.Handle) ?? AppUtil.DefaultIcon();

        var small = new Image { Source = iconSrc, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) };
        var title = Ui.Text(w0.Title, 13, "TextBrush");
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.VerticalAlignment = VerticalAlignment.Center;

        var close = new Chip("✕", 12) { ToolTip = "Закрыть окно" };
        close.Label.Margin = new Thickness(6, 2, 6, 2);
        close.Click += () =>
        {
            Native.PostClose(w0.Handle);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (IsLoaded) Build();
            };
            t.Start();
        };

        var head = new DockPanel { Margin = new Thickness(10, 8, 6, 6) };
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        DockPanel.SetDock(small, Dock.Left);
        head.Children.Add(small);
        head.Children.Add(title);

        // запасной вариант, если миниатюра недоступна (например, окно свёрнуто): большой значок
        var big = new Image { Source = iconSrc, Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(big, BitmapScalingMode.HighQuality);
        var preview = new Border { Margin = new Thickness(10, 0, 10, 10), CornerRadius = new CornerRadius(8), Child = big };
        preview.Dyn(Border.BackgroundProperty, "HoverBrush");
        c.Preview = preview;

        var inner = new Grid();
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(preview, 1);
        inner.Children.Add(head);
        inner.Children.Add(preview);

        var border = new Border
        {
            Width = w,
            Height = h,
            Margin = new Thickness(10),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = inner,
        };
        border.Dyn(Border.BackgroundProperty, "CardBrush");
        border.Dyn(Border.BorderBrushProperty, "CardBorderBrush");
        border.MouseEnter += (_, _) => border.Dyn(Border.BorderBrushProperty, "AccentBrush");
        border.MouseLeave += (_, _) => border.Dyn(Border.BorderBrushProperty, "CardBorderBrush");
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInside(d, close)) return;
            Close();
            Native.Activate(w0.Handle);
        };
        return border;
    }

    static bool IsInside(DependencyObject d, DependencyObject parent)
    {
        while (d != null)
        {
            if (d == parent) return true;
            d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    void PlaceThumbs()
    {
        if (hwnd == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this);

        foreach (var c in cards)
        {
            try
            {
                if (c.Preview.ActualWidth < 10) continue;
                if (DwmRegisterThumbnail(hwnd, c.Win.Handle, out var thumb) != 0 || thumb == IntPtr.Zero) continue;
                if (DwmQueryThumbnailSourceSize(thumb, out var src) != 0 || src.cx <= 0 || src.cy <= 0)
                {
                    DwmUnregisterThumbnail(thumb);
                    continue;
                }

                var origin = c.Preview.TransformToAncestor(this).Transform(new Point(0, 0));
                double px = origin.X * dpi.DpiScaleX, py = origin.Y * dpi.DpiScaleY;
                double pw = c.Preview.ActualWidth * dpi.DpiScaleX, ph = c.Preview.ActualHeight * dpi.DpiScaleY;

                double scale = Math.Min(pw / src.cx, ph / src.cy);
                if (scale > 1) scale = 1; // не растягиваем маленькие окна
                double tw = src.cx * scale, th = src.cy * scale;
                double x = px + (pw - tw) / 2, y = py + (ph - th) / 2;

                var props = new DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = 1 | 4 | 8, // RECTDESTINATION | OPACITY | VISIBLE
                    rcDestination = new RECT { Left = (int)x, Top = (int)y, Right = (int)(x + tw), Bottom = (int)(y + th) },
                    opacity = 255,
                    fVisible = true,
                };
                if (DwmUpdateThumbnailProperties(thumb, ref props) == 0) c.Thumb = thumb;
                else DwmUnregisterThumbnail(thumb);
            }
            catch (Exception ex)
            {
                Settings.Log("thumb: " + ex.Message);
            }
        }
    }
}
