using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace D4;

/// <summary>
/// Основа для виджетов и мини-папок: безрамочное окно-карточка на рабочем столе.
/// Таскается мышью, запоминает положение и держится под обычными окнами.
/// </summary>
public abstract class WidgetWindow : Window
{
    protected readonly Settings S;
    protected readonly Border Card = new();
    readonly string key;
    IntPtr hwnd;

    /// <param name="height">Если высота &lt;= 0, окно подстраивается под содержимое.</param>
    protected WidgetWindow(Settings settings, string key, string title, double width, double height, double defaultX, double defaultY)
    {
        S = settings;
        this.key = key;

        Title = title;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = width;
        if (height > 0) Height = height; else SizeToContent = SizeToContent.Height;

        var pos = S.Positions.TryGetValue(key, out var p) && p.Length == 2 ? p : new[] { defaultX, defaultY };
        Left = pos[0];
        Top = pos[1];

        Card.CornerRadius = new CornerRadius(22);
        Card.Margin = new Thickness(8);
        Card.BorderThickness = new Thickness(1);
        Card.Effect = new DropShadowEffect { BlurRadius = 18, Opacity = .4, ShadowDepth = 2 };
        Card.Dyn(Border.BackgroundProperty, "CardBrush");
        Card.Dyn(Border.BorderBrushProperty, "CardBorderBrush");
        Content = Card;

        MouseLeftButtonDown += OnDrag;
        LocationChanged += (_, _) =>
        {
            S.Positions[key] = new[] { Left, Top };
            S.SaveSoon();
        };
        Loaded += (_, _) => SendBack();
        Deactivated += (_, _) => SendBack();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        hwnd = new WindowInteropHelper(this).Handle;
        Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW); // не показывать в Alt+Tab
    }

    void SendBack()
    {
        if (hwnd != IntPtr.Zero) Native.SendToBottom(hwnd);
    }

    void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1 || IsInside<TextBox>(e.OriginalSource as DependencyObject)) return;
        try { DragMove(); } catch { /* кнопка уже отпущена */ }
    }

    static bool IsInside<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T) return true;
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }
}

public sealed class ClockWidget : WidgetWindow
{
    readonly TextBlock time = Ui.Text("", 54);
    readonly TextBlock date = Ui.Text("", 15, "MutedBrush");
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public ClockWidget(Settings settings) : base(settings, "clock", "D4 Clock", 280, 160, 80, 120)
    {
        time.FontWeight = FontWeights.Light;
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(time);
        panel.Children.Add(date);
        Card.Child = panel;

        timer.Tick += (_, _) => Update();
        Update();
        timer.Start();
        Closed += (_, _) => timer.Stop();
    }

    void Update()
    {
        var now = DateTime.Now;
        var ru = CultureInfo.GetCultureInfo("ru-RU");
        time.Text = now.ToString("HH:mm", ru);
        var d = now.ToString("dddd, d MMMM", ru);
        date.Text = char.ToUpper(d[0], ru) + d.Substring(1);
    }
}

public sealed class MonitorWidget : WidgetWindow
{
    readonly TextBlock cpuLabel = Ui.Text("Процессор", 14);
    readonly TextBlock ramLabel = Ui.Text("Память", 14);
    readonly ProgressBar cpuBar = MakeBar();
    readonly ProgressBar ramBar = MakeBar();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1.5) };

    static ProgressBar MakeBar()
    {
        var b = new ProgressBar { Height = 8, Minimum = 0, Maximum = 100, BorderThickness = new Thickness(0), Margin = new Thickness(0, 4, 0, 14) };
        b.Dyn(Control.ForegroundProperty, "AccentBrush");
        b.Dyn(Control.BackgroundProperty, "HoverBrush");
        return b;
    }

    public MonitorWidget(Settings settings) : base(settings, "monitor", "D4 Monitor", 280, 170, 80, 300)
    {
        var panel = new StackPanel { Margin = new Thickness(20, 16, 20, 4), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(cpuLabel);
        panel.Children.Add(cpuBar);
        panel.Children.Add(ramLabel);
        panel.Children.Add(ramBar);
        Card.Child = panel;

        Native.CpuUsage(); // первый замер служит только точкой отсчёта
        timer.Tick += (_, _) => Update();
        Update();
        timer.Start();
        Closed += (_, _) => timer.Stop();
    }

    void Update()
    {
        double cpu = Native.CpuUsage();
        var (percent, used, total) = Native.Memory();
        cpuLabel.Text = $"Процессор  {cpu:0}%";
        ramLabel.Text = $"Память  {used:0.0} из {total:0.0} ГБ";
        cpuBar.Value = cpu;
        ramBar.Value = percent;
    }
}

public sealed class NotesWidget : WidgetWindow
{
    public NotesWidget(Settings settings) : base(settings, "notes", "D4 Notes", 300, 260, 400, 120)
    {
        var box = new TextBox
        {
            Text = S.Notes,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 15,
            FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 6, 0, 0),
        };
        box.Dyn(Control.ForegroundProperty, "TextBrush");
        box.Dyn(TextBox.CaretBrushProperty, "AccentBrush");
        box.TextChanged += (_, _) =>
        {
            S.Notes = box.Text;
            S.SaveSoon();
        };

        var panel = new DockPanel { Margin = new Thickness(18, 14, 18, 14) };
        var header = Ui.Text("Заметки", 16, "AccentBrush", true);
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(box);
        Card.Child = panel;
    }
}
