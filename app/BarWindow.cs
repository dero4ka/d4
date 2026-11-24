using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace D4;

/// <summary>
/// Свой бар вместо панели задач. По центру — док со значками: закреплённые и запущенные приложения.
/// Значки растут волной под курсором, у запущенных приложений снизу полоска, у активного она длиннее.
/// </summary>
public sealed class BarWindow : Window
{
    sealed class DockItem
    {
        public string Key = "";
        public string? LaunchPath;
        public string? ExePath;
        public bool Pinned;
        public bool Entered;
        public bool Uwp;
        public bool IconReal;
        public int DotState = -1; // -1 неизвестно, 0 не запущено, 1 запущено, 2 активно
        public string Title = "";
        public readonly List<IntPtr> Windows = new();
        public readonly List<string> Titles = new();
        public Border Tile = null!;
        public Image Icon = null!;
        public Border Dot = null!;
        public readonly ScaleTransform Scale = new(1, 1);
    }

    readonly Settings s;
    readonly Action openSettings;
    readonly Action toggleTaskbar;
    readonly Action exit;
    readonly Action<string> setTheme;

    readonly Border outer = new();
    readonly Grid grid = new() { Margin = new Thickness(8, 0, 8, 0) };
    readonly StackPanel dock = new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Background = Brushes.Transparent,
    };
    readonly TextBlock clock = Ui.Text("", 14, "TextBrush", true);
    readonly DispatcherTimer guard = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(1000) };
    readonly EventHandler displayHandler;
    readonly CultureInfo ru = CultureInfo.GetCultureInfo("ru-RU");

    readonly Dictionary<string, DockItem> items = new();
    readonly Dictionary<string, (string Key, string Target)> pinCache = new();
    List<string> order = new();
    int leaving;
    double builtSize = -1;
    string clockText = "";
    IntPtr hwnd;

    Flyout? flyout;
    readonly Chip netChip = new("\uE701", 16);
    readonly Chip volChip = new("\uE767", 16);
    readonly Chip batChip = new("", 13);
    double volume;
    bool muted;
    bool audioOk;
    NetStatus net = new(false, "none", "", null, 0);
    DateTime revealUntil = DateTime.MinValue;
    int tickCount;

    // не называем AtTop как Top: у Window уже есть свойство Top (координата)
    bool AtTop => s.BarEdge != "Bottom";

    double IconSize => Math.Clamp(Math.Round(s.BarHeight * 0.55), 16, 40);

    /// <summary>Насколько значок под курсором вырастает (ограничено высотой бара).</summary>
    double Lift => Math.Max(0.1, Math.Min(0.55, (s.BarHeight - 8.0) / IconSize - 1));

    public BarWindow(Settings settings, Action openSettings, Action toggleTaskbar, Action exit, Action<string> setTheme)
    {
        this.setTheme = setTheme;
        s = settings;
        this.openSettings = openSettings;
        this.toggleTaskbar = toggleTaskbar;
        this.exit = exit;

        Title = "D4 Bar";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = s.BarHeight;
        this.Dyn(BackgroundProperty, "BgBrush");
        GlassFx.Attach(this);

        // слева: меню D4 и кнопка «Пуск»
        var logo = new Chip("D4", 16);
        logo.Label.FontWeight = FontWeights.Bold;
        logo.Click += () => ShowMainMenu(logo);

        var start = new StartButton { ToolTip = "Меню Пуск (клавиша Win)" };
        start.Click += () => Launcher.Toggle(s);

        var searchIcon = Ui.Text("\uE721", 14, "MutedBrush");
        searchIcon.FontFamily = new FontFamily("Segoe MDL2 Assets");
        searchIcon.VerticalAlignment = VerticalAlignment.Center;
        var searchText = Ui.Text("Поиск", 13, "MutedBrush");
        searchText.Margin = new Thickness(8, 0, 0, 0);
        searchText.VerticalAlignment = VerticalAlignment.Center;
        var searchRow = new StackPanel { Orientation = Orientation.Horizontal };
        searchRow.Children.Add(searchIcon);
        searchRow.Children.Add(searchText);
        var searchBox = new Border
        {
            Width = 150,
            Height = Math.Min(30, s.BarHeight - 10),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = Cursors.Hand,
            Child = searchRow,
            ToolTip = "Поиск приложений, файлов и сайтов",
        };
        searchBox.Dyn(Border.BackgroundProperty, "HoverBrush");
        searchBox.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Launcher.Toggle(s);
        };

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(logo);
        left.Children.Add(start);
        left.Children.Add(searchBox);

        // справа: обзор задач, скрытые значки (трей), сеть и часы
        var taskView = new Chip("", 16) { ToolTip = "Все открытые окна" };
        taskView.Label.FontFamily = new FontFamily("Segoe MDL2 Assets");
        taskView.Click += TaskView.Toggle;

        var tray = new Chip("", 14) { ToolTip = "Фоновые приложения и значки трея" };
        tray.Label.FontFamily = new FontFamily("Segoe MDL2 Assets");
        tray.Click += () => ShowTasksFlyout(tray);

        netChip.Label.FontFamily = new FontFamily("Segoe MDL2 Assets");
        netChip.Click += ShowNetworkFlyout;

        volChip.Label.FontFamily = new FontFamily("Segoe MDL2 Assets");
        volChip.Click += ShowVolumeFlyout;
        volChip.PreviewMouseWheel += (_, e) =>
        {
            if (!audioOk) return;
            volume = Math.Clamp(volume + (e.Delta > 0 ? 0.05 : -0.05), 0, 1);
            AudioVolume.SetVolume(volume);
            if (muted && volume > 0) { muted = false; AudioVolume.SetMuted(false); }
            ShowVolume();
            e.Handled = true;
        };

        batChip.Label.FontFamily = new FontFamily("Segoe MDL2 Assets");
        batChip.Visibility = Visibility.Collapsed;
        batChip.Click += ShowBatteryFlyout;

        clock.Margin = new Thickness(8, 0, 4, 0);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        right.Children.Add(taskView);
        right.Children.Add(tray);
        right.Children.Add(volChip);
        right.Children.Add(netChip);
        right.Children.Add(batChip);
        right.Children.Add(clock);

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        left.HorizontalAlignment = HorizontalAlignment.Left;
        right.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(left, 0);
        Grid.SetColumn(dock, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(dock);
        grid.Children.Add(right);

        dock.MouseMove += OnDockMove;
        dock.MouseLeave += OnDockLeave;

        outer.Child = grid;
        outer.Dyn(Border.BorderBrushProperty, "AccentBrush");
        Content = outer;

        ApplyBorder();
        Tick();
        timer.Tick += (_, _) => Tick();
        guard.Tick += (_, _) =>
        {
            try { KeepTaskbarHidden(); } catch { /* следующий тик повторит */ }
        };
        guard.Start();
        timer.Start();

        displayHandler = (_, _) => Dispatcher.Invoke(() => Dock());
        SystemEvents.DisplaySettingsChanged += displayHandler;

        Loaded += (_, _) =>
        {
            Dock();
            PlayIntro();
        };
        Closed += (_, _) =>
        {
            timer.Stop();
            guard.Stop();
            SystemEvents.DisplaySettingsChanged -= displayHandler;
            if (hwnd != IntPtr.Zero) Native.AppBarRemove(hwnd);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        hwnd = new WindowInteropHelper(this).Handle;
        // не показывать в Alt+Tab и не забирать фокус у приложений при клике по бару
        Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        Native.AppBarRegister(hwnd);
    }

    /// <summary>Применяет изменения из настроек (край, высота, закреплённые программы).</summary>
    public void ApplySettings()
    {
        Height = s.BarHeight;
        ApplyBorder();
        if (Math.Abs(IconSize - builtSize) > 0.01) RebuildDock();
        else Refresh();
        Dock();
    }

    void RebuildDock()
    {
        items.Clear();
        dock.Children.Clear();
        order.Clear();
        leaving = 0;
        Refresh();
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

    // ---------- анимации ----------

    void PlayIntro()
    {
        double dy = AtTop ? -14 : 14;
        var shift = new TranslateTransform(0, dy);
        grid.RenderTransform = shift;
        grid.Opacity = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        grid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, TimeSpan.FromMilliseconds(600)) { EasingFunction = ease });
    }

    static void AnimateScale(ScaleTransform t, double to, int ms, IEasingFunction? ease = null)
    {
        ease ??= new CubicEase { EasingMode = EasingMode.EaseOut };
        t.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
        t.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
    }

    static void SetScale(ScaleTransform t, double v)
    {
        t.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        t.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        t.ScaleX = v;
        t.ScaleY = v;
    }

    /// <summary>Значки растут волной: чем ближе к курсору, тем больше.</summary>
    void OnDockMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(dock);
        double lift = Lift;
        foreach (var it in items.Values)
        {
            if (!ReferenceEquals(it.Tile.Parent, dock)) continue;
            var c = it.Tile.TranslatePoint(new Point(it.Tile.ActualWidth / 2, 0), dock);
            double d = p.X - c.X;
            SetScale(it.Scale, 1 + lift * Math.Exp(-(d * d) / (2 * 60 * 60)));
        }
    }

    void OnDockLeave(object sender, MouseEventArgs e)
    {
        foreach (var it in items.Values) AnimateScale(it.Scale, 1, 240);
    }

    void PlayEnter(DockItem it)
    {
        it.Tile.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        it.Scale.ScaleX = 0.4;
        it.Scale.ScaleY = 0.4;
        AnimateScale(it.Scale, 1, 420, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 });
    }

    void Leave(DockItem it)
    {
        if (!dock.Children.Contains(it.Tile)) return;
        leaving++;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            dock.Children.Remove(it.Tile);
            order.Remove(it.Key);
            leaving = Math.Max(0, leaving - 1);
        };
        it.Tile.BeginAnimation(OpacityProperty, fade);
        AnimateScale(it.Scale, 0.4, 180);
    }

    void SetDot(DockItem it, int state)
    {
        if (it.DotState == state) return;
        it.DotState = state;
        double width = state == 2 ? 18 : state == 1 ? 6 : 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        it.Dot.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        it.Dot.BeginAnimation(OpacityProperty, new DoubleAnimation(state == 0 ? 0 : 1, TimeSpan.FromMilliseconds(220)));
    }

    // ---------- меню ----------

    void Open(Flyout f)
    {
        flyout?.Close();
        flyout = f;
        f.Show();
    }

    void ShowMainMenu(FrameworkElement target)
    {
        var f = new Flyout(target, AtTop);
        f.Row(null, "Настройки D4", openSettings);
        f.Row(null, "Меню Пуск и поиск", () => Launcher.Toggle(s));
        f.Row(null, s.HideTaskbar ? "Показать панель задач Windows" : "Скрыть панель задач Windows", toggleTaskbar);
        f.Separator();
        f.Section("Тема");
        var themeList = new StackPanel();
        foreach (var th in ThemeManager.All)
        {
            var id = th.Id;
            f.Row(null, th.Name, () => setTheme(id), ThemeManager.Current.Id == id, null, themeList);
        }
        f.Add(new ScrollViewer { MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = themeList });
        f.Separator();
        f.Row(null, "Выход", exit);
        Open(f);
    }

    /// <summary>Выбор окон приложения и действия с ним.</summary>
    void ShowItemFlyout(DockItem it)
    {
        var f = new Flyout(it.Tile, AtTop);

        if (it.Windows.Count > 0)
        {
            f.Section(it.Windows.Count > 1 ? "Окна (" + it.Windows.Count + ")" : "Окно");
            var fg = Native.Foreground();
            for (int i = 0; i < it.Windows.Count; i++)
            {
                var h = it.Windows[i];
                var title = i < it.Titles.Count && !string.IsNullOrWhiteSpace(it.Titles[i]) ? it.Titles[i] : it.Title;
                f.Row(it.Icon.Source, title, () => Native.Activate(h), h == fg, () => Native.PostClose(h));
            }
            f.Separator();
        }
        else
        {
            f.Section(string.IsNullOrWhiteSpace(it.Title) ? "Приложение" : it.Title);
        }

        var launch = it.LaunchPath;
        if (launch != null)
            f.Row(null, it.Windows.Count == 0 ? "Открыть" : "Открыть ещё одно окно", () => AppUtil.Launch(launch));

        bool required = Settings.IsRequired(it.ExePath) || Settings.IsRequired(it.LaunchPath);
        if (it.Pinned && !required)
        {
            var key = it.Key;
            f.Row(null, "Открепить от бара", () =>
            {
                s.Pinned.RemoveAll(p => ResolvePin(p).Key == key);
                s.Save();
                Refresh();
            });
        }
        else if (!it.Pinned && !it.Uwp && it.ExePath != null)
        {
            var exe = it.ExePath;
            f.Row(null, "Закрепить на баре", () =>
            {
                if (!s.Pinned.Contains(exe)) s.Pinned.Add(exe);
                s.Save();
                Refresh();
            });
        }

        if (it.Windows.Count > 1)
        {
            var all = it.Windows.ToList();
            f.Row(null, "Закрыть все окна", () =>
            {
                foreach (var h in all) Native.PostClose(h);
            });
        }

        if (it.Windows.Count > 0)
        {
            var wins = it.Windows.ToList();
            var exe = it.ExePath;
            f.Separator();
            f.Row(null, "Завершить задачу", () => Native.EndTask(wins, exe));
        }

        Open(f);
    }

    void OnClick(DockItem it)
    {
        if (it.Windows.Count == 0)
        {
            if (it.LaunchPath != null) AppUtil.Launch(it.LaunchPath);
            return;
        }

        if (it.Windows.Count > 1)
        {
            ShowItemFlyout(it); // несколько окон — предлагаем выбрать
            return;
        }

        var fg = Native.Foreground();
        if (it.Windows[0] == fg) Native.Minimize(fg); // повторный клик по активному окну сворачивает его
        else Native.Activate(it.Windows[0]);
    }

    // ---------- сеть и трей ----------

    void UpdateNetwork()
    {
        net = NetInfo.Read();
        netChip.Label.Text = net.Kind == "ethernet" ? "" : "";
        netChip.Opacity = net.Connected ? 1 : 0.4;
        netChip.ToolTip = net.Connected ? net.Adapter + " — подключено" : "Нет подключения к сети";
    }

    void ShowNetworkFlyout()
    {
        UpdateNetwork();
        var st = net;
        var f = new Flyout(netChip, AtTop);

        f.Section("Сеть");
        var title = f.Text(st.Connected ? st.Adapter : "Нет подключения", 16, "TextBrush", true);
        string kind = st.Kind == "wifi" ? "Wi-Fi" : st.Kind == "ethernet" ? "Ethernet" : st.Connected ? "Подключено" : "Проверьте кабель или Wi-Fi";
        var detail = f.Text(kind + (st.Ip != null ? "\nIP: " + st.Ip : "") + (st.SpeedMbps > 0 ? "\nСкорость: " + st.SpeedMbps + " Мбит/с" : ""), 13, "MutedBrush");

        var wifiSlot = f.Slot();
        f.Separator();
        f.Row(null, "Параметры сети", () => AppUtil.Launch("ms-settings:network"));
        f.Row(null, "Все Wi-Fi сети Windows", () => AppUtil.Launch("ms-availablenetworks:"));
        Open(f);

        // netsh медленный — всё делаем в фоне
        Task.Run(() =>
        {
            var cur = NetInfo.ReadWifi();
            var list = NetInfo.ListWifi();
            var saved = NetInfo.SavedProfiles();
            return (cur, list, saved);
        }).ContinueWith(task =>
        {
            if (task.IsFaulted) return;
            var (cur, list, saved) = task.Result;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!f.IsOpen) return;
                if (!string.IsNullOrWhiteSpace(cur.Ssid)) title.Text = cur.Ssid;
                if (cur.Signal != null) detail.Text += "\nСигнал: " + cur.Signal + "%";
                if (list.Count == 0) return;

                f.Separator(wifiSlot);
                f.Section("Доступные Wi-Fi сети", wifiSlot);
                foreach (var n in list.Take(8))
                {
                    var ssid = n.Ssid;
                    bool isCur = ssid == cur.Ssid;
                    bool known = saved.Contains(ssid);
                    string label = ssid + "   " + n.Signal + "%" + (isCur ? "  ✓" : known ? "" : "  🔒");
                    f.Row(null, label, () =>
                    {
                        if (isCur) return;
                        if (known) Task.Run(() => NetInfo.WifiConnect(ssid));
                        else AppUtil.Launch("ms-availablenetworks:"); // нового пароля у нас нет — вводим в системном окне
                    }, isCur, null, wifiSlot);
                }
                if (cur.Ssid != null)
                    f.Row(null, "Отключиться от Wi-Fi", () => Task.Run(NetInfo.WifiDisconnect), false, null, wifiSlot);
            }));
        });
    }

    // ---------- громкость ----------

    void UpdateAudio()
    {
        audioOk = AudioVolume.TryGet(out volume, out muted);
        volChip.Visibility = audioOk ? Visibility.Visible : Visibility.Collapsed;
        ShowVolume();
    }

    void ShowVolume()
    {
        volChip.Label.Text = muted || volume <= 0.001 ? "\uE74F" : volume < 0.34 ? "\uE992" : volume < 0.67 ? "\uE993" : "\uE767";
        volChip.ToolTip = muted ? "Звук выключен" : "Громкость " + (int)Math.Round(volume * 100) + "%";
    }

    void ShowVolumeFlyout()
    {
        UpdateAudio();
        if (!audioOk) return;
        var f = new Flyout(volChip, AtTop);
        f.Section("Громкость");
        var pct = f.Text((int)Math.Round(volume * 100) + "%", 20, "TextBrush", true);
        f.Slider(volume, v =>
        {
            volume = v;
            AudioVolume.SetVolume(v);
            if (muted && v > 0) { muted = false; AudioVolume.SetMuted(false); }
            pct.Text = (int)Math.Round(v * 100) + "%";
            ShowVolume();
        });
        f.Separator();
        f.Row(null, muted ? "Включить звук" : "Выключить звук", () =>
        {
            muted = !muted;
            AudioVolume.SetMuted(muted);
            ShowVolume();
        });
        f.Row(null, "Параметры звука", () => AppUtil.Launch("ms-settings:sound"));
        Open(f);
    }

    // ---------- батарея ----------

    void UpdateBattery()
    {
        var p = System.Windows.Forms.SystemInformation.PowerStatus;
        bool has = p.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery
                   && p.BatteryLifePercent <= 1.0;
        batChip.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (!has) return;
        int pct = (int)Math.Round(p.BatteryLifePercent * 100);
        bool charging = p.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
        batChip.Label.Text = (charging ? "\uE83E " : "\uEBA0 ") + pct + "%";
        batChip.ToolTip = (charging ? "Заряжается: " : "Батарея: ") + pct + "%";
    }

    void ShowBatteryFlyout()
    {
        UpdateBattery();
        var p = System.Windows.Forms.SystemInformation.PowerStatus;
        var f = new Flyout(batChip, AtTop);
        f.Section("Питание");
        f.Text((int)Math.Round(p.BatteryLifePercent * 100) + "%", 20, "TextBrush", true);
        string state = p.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? "Подключено к питанию" : "Работа от батареи";
        int secs = p.BatteryLifeRemaining;
        if (secs > 0 && p.PowerLineStatus != System.Windows.Forms.PowerLineStatus.Online)
            state += "\nОсталось примерно " + secs / 3600 + " ч " + secs % 3600 / 60 + " мин";
        f.Text(state, 13, "MutedBrush");
        f.Separator();
        f.Row(null, "Параметры питания", () => AppUtil.Launch("ms-settings:batterysaver"));
        Open(f);
    }

    /// <summary>Фоновые программы (то, что висит в трее) с возможностью завершить задачу.</summary>
    void ShowTasksFlyout(FrameworkElement target)
    {
        var f = new Flyout(target, AtTop);
        f.Section("Фоновые приложения");
        var slot = f.Slot();
        f.Text("Загрузка…", 13, "MutedBrush", false, slot);
        f.Separator();
        f.Row(null, "Значки трея Windows (на 12 секунд)", RevealTray);
        f.Row(null, "Диспетчер задач", () => AppUtil.Launch("taskmgr.exe"));
        Open(f);

        var shown = new HashSet<string>(items.Keys);
        Task.Run(() => Native.BackgroundApps(shown)).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!f.IsOpen) return;
                slot.Children.Clear();
                if (task.IsFaulted || task.Result.Count == 0)
                {
                    f.Text("Фоновых приложений нет", 13, "MutedBrush", false, slot);
                    return;
                }
                var grid = new WrapPanel { MaxWidth = 300, Margin = new Thickness(4, 2, 4, 2) };
                foreach (var p in task.Result)
                    grid.Children.Add(BackgroundTile(p, grid));
                f.Add(grid, slot);
            }));
        });
    }

    /// <summary>Значок фонового приложения: название при наведении, крестик для завершения задачи.</summary>
    FrameworkElement BackgroundTile(Native.ProcInfo info, WrapPanel owner)
    {
        var img = new Image { Source = AppUtil.GetIcon(info.Path) ?? AppUtil.DefaultIcon(), Width = 30, Height = 30 };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        var x = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            ToolTip = "Завершить задачу",
            Child = new TextBlock { Text = "✕", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White },
        };
        x.Background = new SolidColorBrush(Color.FromRgb(0xE0, 0x4B, 0x5A));

        var cell = new Grid { Width = 56, Height = 56 };
        cell.Children.Add(img);
        cell.Children.Add(x);

        var tile = new Border
        {
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(2),
            Background = Brushes.Transparent,
            Child = cell,
            ToolTip = info.Name + (info.Pids.Length > 1 ? " (процессов: " + info.Pids.Length + ")" : "") + "\n" + info.MemMb + " МБ",
        };
        ToolTipService.SetInitialShowDelay(tile, 150);
        tile.MouseEnter += (_, _) =>
        {
            tile.Dyn(Border.BackgroundProperty, "HoverBrush");
            x.Visibility = Visibility.Visible;
        };
        tile.MouseLeave += (_, _) =>
        {
            tile.Background = Brushes.Transparent;
            x.Visibility = Visibility.Collapsed;
        };
        x.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Native.EndProcesses(info.Pids);
            owner.Children.Remove(tile);
        };
        return tile;
    }

    /// <summary>Показывает настоящую панель Windows на несколько секунд — там значки трея и фоновых приложений.</summary>
    void RevealTray()
    {
        revealUntil = DateTime.UtcNow.AddSeconds(12);
        Native.SetTaskbarVisible(true);
    }

    // ---------- док ----------

    (string Key, string Target) ResolvePin(string path)
    {
        if (pinCache.TryGetValue(path, out var cached)) return cached;
        string target = path;
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            target = AppUtil.ResolveShortcut(path) ?? path;
        var value = (target.ToLowerInvariant(), target);
        pinCache[path] = value;
        return value;
    }

    ImageSource? LoadIcon(DockItem it, IntPtr window)
    {
        ImageSource? img = null;
        if (!it.Uwp && it.ExePath != null) img = AppUtil.GetIcon(it.ExePath);
        if (img == null && window != IntPtr.Zero) img = AppUtil.GetWindowIcon(window);
        if (img == null && it.ExePath != null) img = AppUtil.GetIcon(it.ExePath);
        return img;
    }

    DockItem Ensure(string key)
    {
        if (items.TryGetValue(key, out var existing)) return existing;

        double size = IconSize;
        builtSize = size;
        var it = new DockItem { Key = key };

        it.Icon = new Image
        {
            Width = size,
            Height = size,
            RenderTransform = it.Scale,
            RenderTransformOrigin = new Point(.5, .5),
        };
        RenderOptions.SetBitmapScalingMode(it.Icon, BitmapScalingMode.HighQuality);

        it.Dot = new Border
        {
            Width = 0,
            Height = 3,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, -7),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        it.Dot.Dyn(Border.BackgroundProperty, "AccentBrush");

        var cell = new Grid();
        cell.Children.Add(it.Icon);
        cell.Children.Add(it.Dot);

        var tile = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            Padding = new Thickness(7, 6, 7, 8),
            Margin = new Thickness(1, 0, 1, 0),
            Child = cell,
            Cursor = Cursors.Hand,
            Opacity = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        tile.MouseEnter += (_, _) => tile.Dyn(Border.BackgroundProperty, "HoverBrush");
        tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
        tile.MouseLeftButtonUp += (_, _) => OnClick(it);
        tile.MouseRightButtonUp += (_, _) => ShowItemFlyout(it);
        it.Tile = tile;

        items[key] = it;
        return it;
    }

    void Refresh()
    {
        var wins = Native.GetTaskWindows();
        var fg = Native.Foreground();

        foreach (var it in items.Values)
        {
            it.Windows.Clear();
            it.Titles.Clear();
            it.Pinned = false;
        }

        var keys = new List<string>();

        // закреплённые — всегда на баре, в заданном порядке
        foreach (var pin in s.Pinned)
        {
            var (key, target) = ResolvePin(pin);
            if (keys.Contains(key)) continue;
            var it = Ensure(key);
            it.Pinned = true;
            it.LaunchPath = pin;
            it.ExePath ??= target;
            it.Title = System.IO.Path.GetFileNameWithoutExtension(pin);
            keys.Add(key);
        }

        // запущенные: окна одного приложения собираются в один значок
        foreach (var w in wins)
        {
            var key = w.ExePath?.ToLowerInvariant() ?? "hwnd:" + w.Handle.ToInt64();
            var it = Ensure(key);
            it.ExePath ??= w.ExePath;
            if (!it.Pinned && !w.Uwp) it.LaunchPath ??= w.ExePath;
            it.Uwp |= w.Uwp;
            it.Windows.Add(w.Handle);
            it.Titles.Add(w.Title);
            if (it.Windows.Count == 1) it.Title = w.Title;
            if (!keys.Contains(key)) keys.Add(key);
        }

        // приложения, которых больше нет
        foreach (var gone in items.Keys.Where(k => !keys.Contains(k)).ToList())
        {
            var it = items[gone];
            items.Remove(gone);
            Leave(it);
        }

        // значки
        foreach (var key in keys)
        {
            var it = items[key];
            if (it.IconReal) continue;
            var img = LoadIcon(it, it.Windows.Count > 0 ? it.Windows[0] : IntPtr.Zero);
            if (img != null)
            {
                it.Icon.Source = img;
                it.IconReal = true;
            }
            else if (it.Icon.Source == null)
            {
                it.Icon.Source = AppUtil.DefaultIcon();
            }
        }

        // порядок и появление новых значков
        if (leaving == 0 && !order.SequenceEqual(keys))
        {
            dock.Children.Clear();
            foreach (var key in keys) dock.Children.Add(items[key].Tile);
            order = keys.ToList();
            foreach (var key in keys)
            {
                var it = items[key];
                if (it.Entered) continue;
                it.Entered = true;
                PlayEnter(it);
            }
        }

        // подсказки и индикаторы
        foreach (var key in keys)
        {
            var it = items[key];
            it.Tile.ToolTip = string.IsNullOrWhiteSpace(it.Title) ? it.Key : it.Title;
            int state = it.Windows.Count == 0 ? 0 : it.Windows.Contains(fg) ? 2 : 1;
            SetDot(it, state);
        }
    }

    /// <summary>Explorer и некоторые действия (Win+клавиша, наведение на край) возвращают панель задач — прячем её снова.</summary>
    void KeepTaskbarHidden()
    {
        if (s.HideTaskbar && Native.IsTaskbarVisible())
        {
            var now = DateTime.UtcNow;
            if (now < revealUntil)
            {
                // пока вы пользуетесь треем, панель не прячем
                if (System.Windows.Forms.Cursor.Position.Y >= Native.GetSystemMetrics(Native.SM_CYSCREEN) - 300)
                {
                    var extended = now.AddSeconds(3);
                    if (extended > revealUntil) revealUntil = extended;
                }
            }
            else
            {
                Native.SetTaskbarVisible(false);
            }
        }
    }

    void Tick()
    {
        try
        {
            var text = DateTime.Now.ToString("ddd d MMM  HH:mm", ru);
            if (text != clockText)
            {
                clockText = text;
                clock.Text = text;
            }

            KeepTaskbarHidden();

            if (tickCount++ % 5 == 0)
            {
                UpdateNetwork();
                UpdateAudio();
                UpdateBattery();
            }

            // над окном на весь экран (игра, видео) бар не торчит
            bool fullscreen = Native.IsForegroundFullscreen();
            if (fullscreen && IsVisible) Hide();
            else if (!fullscreen && !IsVisible) Show();

            Refresh();
        }
        catch (Exception ex)
        {
            Settings.Log("bar: " + ex.Message);
        }
    }
}
