using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace D4;

internal sealed record AppEntry(string Name, string Path);

/// <summary>Список установленных программ: ярлыки из меню «Пуск» плюс несколько системных пунктов.</summary>
internal static class AppCatalog
{
    static List<AppEntry>? cache;
    static DateTime loadedAt;

    public static List<AppEntry> Get()
    {
        if (cache != null && (DateTime.UtcNow - loadedAt).TotalMinutes < 5) return cache;

        var map = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var dir in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                     Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                 })
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*.lnk", opts))
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (Skip(name)) continue;
                    map.TryAdd(name, new AppEntry(name, f));
                }
            }
            catch (Exception ex)
            {
                Settings.Log("catalog: " + ex.Message);
            }
        }

        var sys = Environment.SystemDirectory;
        var extra = new (string Name, string Path)[]
        {
            ("Параметры Windows", "ms-settings:"),
            ("Проводник", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")),
            ("Диспетчер задач", System.IO.Path.Combine(sys, "taskmgr.exe")),
            ("Панель управления", System.IO.Path.Combine(sys, "control.exe")),
            ("Командная строка", System.IO.Path.Combine(sys, "cmd.exe")),
            ("Калькулятор", "calculator:"),
        };
        foreach (var (n, p) in extra) map.TryAdd(n, new AppEntry(n, p));

        cache = map.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        loadedAt = DateTime.UtcNow;
        return cache;
    }

    static bool Skip(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("uninstall") || n.Contains("удалить") || n.Contains("удаление") || n.Contains("readme");
    }
}

/// <summary>Своё меню «Пуск» и поиск в стиле темы D4.</summary>
public static class Launcher
{
    static LauncherWindow? window;
    static DateTime closedAt = DateTime.MinValue;

    public static void Toggle(Settings s)
    {
        if (window != null)
        {
            window.Close();
            return;
        }
        // клик по кнопке отнимает фокус у меню и закрывает его — тут же заново не открываем
        if ((DateTime.UtcNow - closedAt).TotalMilliseconds < 350) return;

        try
        {
            var w = new LauncherWindow(s);
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
            Settings.Log("launcher: " + ex);
        }
    }
}

internal sealed class LauncherWindow : Window
{
    sealed class Row
    {
        public Border Box = null!;
        public Action Run = null!;
    }

    readonly Settings s;
    readonly TextBox box = new();
    readonly TextBlock hint = Ui.Text("Поиск приложений, файлов и сайтов", 15, "MutedBrush");
    readonly StackPanel list = new();
    readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly Border card = new();
    readonly TranslateTransform slide = new();
    readonly List<Row> rows = new();
    readonly Queue<(Image Img, string Path)> iconQueue = new();
    int sel;
    bool pumping;
    int searchVersion;
    StackPanel? fileSlot;
    int fileInsertAt;
    Flyout? power;

    public LauncherWindow(Settings settings)
    {
        s = settings;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "D4 Launcher";

        double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight;
        Width = 600;
        Height = Math.Min(720, sh - s.BarHeight - 40);
        Left = (sw - Width) / 2;
        Top = s.BarEdge == "Bottom" ? sh - s.BarHeight - Height - 4 : s.BarHeight + 4;

        card.Margin = new Thickness(16);
        card.CornerRadius = new CornerRadius(ThemeManager.Glass ? 30 : 20);
        card.BorderThickness = new Thickness(1);
        card.Effect = new DropShadowEffect { BlurRadius = 24, Opacity = .5, ShadowDepth = 4 };
        card.RenderTransform = slide;
        card.Dyn(Border.BackgroundProperty, "BgBrush");
        card.Dyn(Border.BorderBrushProperty, "CardBorderBrush");

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.Child = grid;
        Content = card;

        // строка поиска
        var icon = Ui.Text("", 16, "MutedBrush");
        icon.FontFamily = new FontFamily("Segoe MDL2 Assets");
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(14, 0, 8, 0);

        box.BorderThickness = new Thickness(0);
        box.Background = Brushes.Transparent;
        box.FontSize = 16;
        box.VerticalContentAlignment = VerticalAlignment.Center;
        box.Padding = new Thickness(0, 10, 12, 10);
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.SetResourceReference(TextBox.CaretBrushProperty, "TextBrush");
        box.TextChanged += (_, _) =>
        {
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Rebuild(box.Text.Trim());
        };

        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(0, 0, 0, 0);

        var inputArea = new Grid();
        inputArea.Children.Add(hint);
        inputArea.Children.Add(box);

        var searchRow = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        searchRow.Children.Add(icon);
        searchRow.Children.Add(inputArea);

        var search = new Border { CornerRadius = new CornerRadius(14), Margin = new Thickness(16, 16, 16, 8), Child = searchRow };
        search.Dyn(Border.BackgroundProperty, "HoverBrush");
        Grid.SetRow(search, 0);
        grid.Children.Add(search);

        // список
        list.Margin = new Thickness(10, 0, 10, 0);
        scroll.Content = list;
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);

        // низ: пользователь, настройки, питание
        var footer = new DockPanel { Margin = new Thickness(16, 8, 12, 12) };
        var power = new Chip("", 16) { ToolTip = "Питание" };
        power.Label.FontFamily = new FontFamily("Segoe MDL2 Assets");
        power.Click += () => ShowPower(power);
        DockPanel.SetDock(power, Dock.Right);
        footer.Children.Add(power);

        var gear = new Chip("", 16) { ToolTip = "Параметры Windows" };
        gear.Label.FontFamily = new FontFamily("Segoe MDL2 Assets");
        gear.Click += () => RunAndClose("ms-settings:");
        DockPanel.SetDock(gear, Dock.Right);
        footer.Children.Add(gear);

        var avatar = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 10, 0) };
        avatar.Dyn(Border.BackgroundProperty, "AccentGradBrush");
        var letter = Ui.Text(Environment.UserName.Length > 0 ? Environment.UserName.Substring(0, 1).ToUpperInvariant() : "D", 14, "BgBrush", true);
        letter.HorizontalAlignment = HorizontalAlignment.Center;
        letter.VerticalAlignment = VerticalAlignment.Center;
        avatar.Child = letter;
        var who = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(avatar);
        who.Children.Add(Ui.Text(Environment.UserName, 14, "TextBrush"));
        footer.Children.Add(who);
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsActive && !(this.power?.IsOpen ?? false)) Close();
        }));
        Loaded += (_, _) =>
        {
            Native.ForceForeground(new WindowInteropHelper(this).Handle);
            box.Focus();
            Keyboard.Focus(box);

            card.Opacity = 0;
            slide.Y = 0;
            if (ThemeManager.Glass)
            {
                card.UpdateLayout();
                var glassBrush = GlassBackdrop.Bake(card);
                if (glassBrush != null) card.Background = glassBrush;
            }
            double from = s.BarEdge == "Bottom" ? 14 : -14;
            slide.Y = from;
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)));
            slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        };

        Rebuild("");
    }

    // ---------- действия ----------

    void RunAndClose(string path)
    {
        Close();
        AppUtil.Launch(path);
    }

    void RunCommand(string file, string args)
    {
        Close();
        try { Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true }); }
        catch (Exception ex) { Settings.Log("power: " + ex.Message); }
    }

    void ShowPower(FrameworkElement target)
    {
        var f = new Flyout(target, below: false);
        f.Row(null, "Заблокировать", () => RunCommand("rundll32.exe", "user32.dll,LockWorkStation"));
        f.Row(null, "Сон", () => RunCommand("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0"));
        f.Row(null, "Выйти из системы", () => RunCommand("shutdown", "/l"));
        f.Separator();
        f.Row(null, "Перезагрузка", () => RunCommand("shutdown", "/r /t 0"));
        f.Row(null, "Выключение", () => RunCommand("shutdown", "/s /t 0"));
        power = f;
        f.Show();
    }

    // ---------- список ----------

    const string EnKeys = "qwertyuiop[]asdfghjkl;'zxcvbnm,.";
    const string RuKeys = "йцукенгшщзхъфывапролджэячсмитьбю";

    /// <summary>Что получилось бы, если бы раскладка была другой («рфздщ» ↔ «hello»).</summary>
    static string SwapLayout(string q)
    {
        var sb = new System.Text.StringBuilder(q.Length);
        foreach (var ch in q)
        {
            int i = EnKeys.IndexOf(ch);
            if (i >= 0) { sb.Append(RuKeys[i]); continue; }
            int j = RuKeys.IndexOf(ch);
            sb.Append(j >= 0 ? EnKeys[j] : ch);
        }
        return sb.ToString();
    }

    void Section(string text)
    {
        var t = Ui.Text(text, 12, "MutedBrush", true);
        t.Margin = new Thickness(10, 10, 10, 4);
        list.Children.Add(t);
    }

    void Rebuild(string q)
    {
        list.Children.Clear();
        rows.Clear();
        iconQueue.Clear();
        sel = 0;
        searchVersion++;
        fileSlot = null;

        var apps = AppCatalog.Get();

        if (q.Length == 0)
        {
            if (s.Pinned.Count > 0)
            {
                Section("Закреплено");
                var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
                foreach (var p in s.Pinned.Take(18)) wrap.Children.Add(Tile(p));
                list.Children.Add(wrap);
            }
            Section("Все приложения");
            foreach (var a in apps) AddRow(a.Name, a.Path);
        }
        else
        {
            var ql = q.ToLowerInvariant();
            var qs = SwapLayout(ql); // запрос, набранный не в той раскладке
            var found = apps
                .Where(a => a.Name.ToLowerInvariant().Contains(ql) || a.Name.ToLowerInvariant().Contains(qs))
                .OrderBy(a => a.Name.ToLowerInvariant().StartsWith(ql) ? 0 : a.Name.ToLowerInvariant().Split(' ').Any(w => w.StartsWith(ql)) ? 1 : 2)
                .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(30)
                .ToList();

            if (found.Count > 0)
            {
                Section("Приложения");
                foreach (var a in found) AddRow(a.Name, a.Path);
            }

            fileSlot = new StackPanel();
            fileInsertAt = rows.Count;
            list.Children.Add(fileSlot);

            Section("Ещё");
            bool looksLikePath = q.Contains('\\') || q.Contains('/') || q.Contains("://") || q.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
            if (looksLikePath || (found.Count == 0 && File.Exists(q)))
                AddRow("Открыть: " + q, q, icon: "");
            AddRow("Искать в интернете: " + q, "https://www.bing.com/search?q=" + Uri.EscapeDataString(q), icon: "");
        }

        Highlight();
        PumpIcons();
        if (q.Length >= 2) StartFileSearch(q);
    }

    void StartFileSearch(string q)
    {
        int v = searchVersion;
        Task.Run(async () =>
        {
            await Task.Delay(250); // ждём, пока человек допечатает
            if (v != searchVersion) return new List<(string Path, bool Dir)>();
            return FileSearch.Find(q, () => v != searchVersion);
        }).ContinueWith(t =>
        {
            if (t.IsFaulted) return;
            var result = t.Result;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (v != searchVersion || fileSlot == null || result.Count == 0) return;
                var header = Ui.Text("Папки и файлы", 12, "MutedBrush", true);
                header.Margin = new Thickness(10, 10, 10, 4);
                fileSlot.Children.Add(header);
                int k = 0;
                foreach (var (path, dir) in result)
                {
                    var name = System.IO.Path.GetFileName(path);
                    if (name.Length == 0) name = path;
                    var shownName = name + "   " + System.IO.Path.GetDirectoryName(path);
                    AddRow(shownName, path, dir ? "\uE8B7" : null, fileSlot, fileInsertAt + k++);
                }
                Highlight(false);
                PumpIcons();
            }));
        });
    }

    FrameworkElement Tile(string path)
    {
        var img = new Image { Width = 34, Height = 34 };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        iconQueue.Enqueue((img, path));

        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var label = Ui.Text(name, 11, "TextBrush");
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.TextAlignment = TextAlignment.Center;
        label.Margin = new Thickness(0, 4, 0, 0);

        var sp = new StackPanel { Width = 86, Margin = new Thickness(2) };
        sp.Children.Add(img);
        sp.Children.Add(label);

        var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(4, 8, 4, 8), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = sp, ToolTip = name };
        b.MouseEnter += (_, _) => b.Dyn(Border.BackgroundProperty, "HoverBrush");
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, _) => RunAndClose(path);
        return b;
    }

    void AddRow(string name, string path, string? icon = null, StackPanel? into = null, int insertAt = -1)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (icon != null)
        {
            var glyph = Ui.Text(icon, 18, "AccentBrush");
            glyph.FontFamily = new FontFamily("Segoe MDL2 Assets");
            glyph.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(glyph);
        }
        else
        {
            var img = new Image { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            iconQueue.Enqueue((img, path));
            grid.Children.Add(img);
        }

        var text = Ui.Text(name, 14, "TextBrush");
        text.VerticalAlignment = VerticalAlignment.Center;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var row = new Row
        {
            Box = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = grid },
            Run = () => RunAndClose(path),
        };
        row.Box.MouseEnter += (_, _) =>
        {
            sel = rows.IndexOf(row);
            Highlight(false);
        };
        row.Box.MouseLeftButtonUp += (_, _) => row.Run();
        if (insertAt >= 0 && insertAt <= rows.Count) rows.Insert(insertAt, row);
        else rows.Add(row);
        (into ?? list).Children.Add(row.Box);
    }

    void Highlight(bool scrollTo = true)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (i == sel) rows[i].Box.Dyn(Border.BackgroundProperty, "HoverBrush");
            else rows[i].Box.Background = Brushes.Transparent;
        }
        if (scrollTo && sel >= 0 && sel < rows.Count) rows[sel].Box.BringIntoView();
    }

    /// <summary>Значки подгружаются порциями, чтобы окно открывалось сразу.</summary>
    void PumpIcons()
    {
        if (pumping) return;
        pumping = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(PumpStep));
    }

    void PumpStep()
    {
        int n = 0;
        while (iconQueue.Count > 0 && n++ < 6)
        {
            var (img, path) = iconQueue.Dequeue();
            img.Source = AppUtil.GetIcon(path) ?? AppUtil.DefaultIcon();
        }
        if (iconQueue.Count > 0) Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(PumpStep));
        else pumping = false;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Down:
                if (rows.Count > 0) { sel = Math.Min(rows.Count - 1, sel + 1); Highlight(); }
                e.Handled = true;
                break;
            case Key.Up:
                if (rows.Count > 0) { sel = Math.Max(0, sel - 1); Highlight(); }
                e.Handled = true;
                break;
            case Key.Enter:
                if (sel >= 0 && sel < rows.Count) rows[sel].Run();
                e.Handled = true;
                break;
        }
    }
}
