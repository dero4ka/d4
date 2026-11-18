using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace D4;

/// <summary>Свой бар вместо панели задач: меню D4, закреплённые программы, открытые окна и часы.</summary>
public sealed class BarWindow : Window
{
    readonly Settings s;
    readonly Action openSettings;
    readonly Action toggleTaskbar;
    readonly Action exit;

    readonly Border outer = new();
    readonly StackPanel launchers = new() { Orientation = Orientation.Horizontal };
    readonly StackPanel tasks = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock clock = Ui.Text("", 14, "TextBrush", true);
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly EventHandler displayHandler;

    IntPtr hwnd;
    string taskSignature = "";

    // не называем AtTop как Top: у Window уже есть свойство Top (координата)
    bool AtTop => s.BarEdge != "Bottom";

    public BarWindow(Settings settings, Action openSettings, Action toggleTaskbar, Action exit)
    {
        s = settings;
        this.openSettings = openSettings;
        this.toggleTaskbar = toggleTaskbar;
        this.exit = exit;

        Title = "D4 Bar";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = s.BarHeight;
        this.Dyn(BackgroundProperty, "BgBrush");

        // левая часть: логотип + закреплённые программы
        var logo = new Chip("D4", 16);
        logo.Label.FontWeight = FontWeights.Bold;
        logo.Click += () => ShowMenu(logo);
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(logo);
        left.Children.Add(launchers);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        right.Children.Add(clock);

        var grid = new Grid { Margin = new Thickness(8, 0, 8, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(tasks, 1);
        Grid.SetColumn(right, 2);
        tasks.Margin = new Thickness(12, 0, 12, 0);
        grid.Children.Add(left);
        grid.Children.Add(tasks);
        grid.Children.Add(right);

        outer.Child = grid;
        outer.Dyn(Border.BorderBrushProperty, "AccentBrush");
        Content = outer;

        RebuildLaunchers();
        ApplyBorder();
        Tick();
        timer.Tick += (_, _) => Tick();
        timer.Start();

        displayHandler = (_, _) => Dispatcher.Invoke(() => Dock());
        SystemEvents.DisplaySettingsChanged += displayHandler;

        Loaded += (_, _) => Dock();
        Closed += (_, _) =>
        {
            timer.Stop();
            SystemEvents.DisplaySettingsChanged -= displayHandler;
            if (hwnd != IntPtr.Zero) Native.AppBarRemove(hwnd);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        hwnd = new WindowInteropHelper(this).Handle;
        Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW);
        Native.AppBarRegister(hwnd);
    }

    /// <summary>Применяет изменения из настроек (край, высота, закреплённые программы).</summary>
    public void ApplySettings()
    {
        Height = s.BarHeight;
        ApplyBorder();
        RebuildLaunchers();
        Dock();
    }

    void ApplyBorder() =>
        outer.BorderThickness = AtTop ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0);

    void Dock()
    {
        if (hwnd == IntPtr.Zero) return;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
        int thickness = (int)Math.Round(s.BarHeight * dpi);
        var rc = Native.AppBarSetPos(hwnd, AtTop, thickness);
        Native.MoveWindow(hwnd, rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top, true);
    }

    void ShowMenu(FrameworkElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var settingsItem = new MenuItem { Header = "Настройки D4" };
        settingsItem.Click += (_, _) => openSettings();
        var taskbarItem = new MenuItem { Header = s.HideTaskbar ? "Показать панель задач Windows" : "Скрыть панель задач Windows" };
        taskbarItem.Click += (_, _) => toggleTaskbar();
        var exitItem = new MenuItem { Header = "Выход" };
        exitItem.Click += (_, _) => exit();
        menu.Items.Add(settingsItem);
        menu.Items.Add(taskbarItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        menu.IsOpen = true;
    }

    void RebuildLaunchers()
    {
        launchers.Children.Clear();
        foreach (var path in s.Pinned.ToList())
        {
            var p = path;
            var item = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = Brushes.Transparent,
                Margin = new Thickness(2, 0, 2, 0),
                Padding = new Thickness(6),
                Cursor = Cursors.Hand,
                ToolTip = System.IO.Path.GetFileNameWithoutExtension(p),
            };
            var icon = AppUtil.GetIcon(p);
            item.Child = icon != null
                ? new Image { Source = icon, Width = 22, Height = 22 }
                : Ui.Text(System.IO.Path.GetFileNameWithoutExtension(p).FirstOrDefault().ToString(), 14, "TextBrush", true);
            item.MouseEnter += (_, _) => item.Dyn(Border.BackgroundProperty, "HoverBrush");
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            item.MouseLeftButtonUp += (_, _) => AppUtil.Launch(p);
            launchers.Children.Add(item);
        }
    }

    void Tick()
    {
        clock.Text = DateTime.Now.ToString("ddd d MMM  HH:mm", CultureInfo.GetCultureInfo("ru-RU"));
        RefreshTasks();
    }

    void RefreshTasks()
    {
        var list = Native.GetTaskWindows();
        var signature = string.Join("|", list.Select(x => x.Handle.ToInt64() + ":" + x.Title));
        if (signature == taskSignature) return;
        taskSignature = signature;

        tasks.Children.Clear();
        foreach (var (handle, title) in list)
        {
            var h = handle;
            var label = title.Length > 22 ? title.Substring(0, 22) + "…" : title;
            var chip = new Chip(label, 13) { ToolTip = title };
            chip.Click += () => Native.Activate(h);
            tasks.Children.Add(chip);
        }
    }
}
