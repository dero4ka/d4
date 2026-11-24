using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace D4;

/// <summary>Окно настроек: темы, бар, виджеты, обои, мини-папки, автозапуск.</summary>
public sealed class SettingsWindow : Window
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "D4";

    readonly Settings s;
    readonly Action changed;
    readonly Action addFolder;
    readonly StackPanel page = new() { Margin = new Thickness(24, 20, 24, 24) };
    readonly List<(Chip Chip, string Id)> themeChips = new();
    readonly List<(Chip Chip, string Edge)> edgeChips = new();
    readonly ListBox pinned = new() { Height = 110, BorderThickness = new Thickness(1) };

    public SettingsWindow(Settings settings, Action changed, Action addFolder)
    {
        s = settings;
        this.changed = changed;
        this.addFolder = addFolder;

        Title = "D4 — настройки";
        Width = 540;
        Height = 700;
        MinWidth = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.Dyn(BackgroundProperty, "BgBrush");

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };

        GlassFx.Attach(this);
        // сбой в одном разделе не должен мешать открыться всему окну
        foreach (var (name, build) in new (string, Action)[]
        {
            ("themes", BuildThemes), ("bar", BuildBar), ("widgets", BuildWidgets),
            ("wallpaper", BuildWallpaper), ("folders", BuildFolders), ("system", BuildSystem),
        })
        {
            try { build(); }
            catch (Exception ex) { Settings.Log("settings section " + name + ": " + ex); }
        }

        // тема могла смениться из меню бара — синхронизируем рамку на чипах
        ThemeManager.Changed += SyncThemeChips;
        Closed += (_, _) => ThemeManager.Changed -= SyncThemeChips;
    }

    void SyncThemeChips()
    {
        var id = ThemeManager.Current.Id;
        foreach (var (c, cid) in themeChips) c.SetSelected(cid == id);
    }

    // ---------- вспомогательные ----------

    StackPanel Section(string title)
    {
        var header = Ui.Text(title, 18, "AccentBrush", true);
        header.Margin = new Thickness(0, 18, 0, 8);
        page.Children.Add(header);

        var body = new StackPanel();
        var card = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(14), BorderThickness = new Thickness(1), Child = body };
        card.Dyn(Border.BackgroundProperty, "CardBrush");
        card.Dyn(Border.BorderBrushProperty, "CardBorderBrush");
        page.Children.Add(card);
        return body;
    }

    void Field(StackPanel into, string label, string value, Action<string> set)
    {
        var l = Ui.Text(label, 12, "MutedBrush");
        l.Margin = new Thickness(0, 8, 0, 2);
        into.Children.Add(l);

        var tb = new TextBox { Text = value, Padding = new Thickness(8, 5, 8, 5), FontSize = 14, BorderThickness = new Thickness(1) };
        tb.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        tb.SetResourceReference(TextBox.CaretBrushProperty, "TextBrush");
        tb.SetResourceReference(Control.BackgroundProperty, "HoverBrush");
        tb.SetResourceReference(Control.BorderBrushProperty, "CardBorderBrush");
        tb.LostFocus += (_, _) =>
        {
            set(tb.Text.Trim());
            Commit();
        };
        into.Children.Add(tb);
    }

    CheckBox Check(StackPanel into, string text, bool value, Action<bool> set)
    {
        var cb = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 4, 0, 4), FontSize = 14 };
        cb.Dyn(Control.ForegroundProperty, "TextBrush");
        cb.Checked += (_, _) => { set(true); Commit(); };
        cb.Unchecked += (_, _) => { set(false); Commit(); };
        into.Children.Add(cb);
        return cb;
    }

    Chip Button(string text, Action click)
    {
        var chip = new Chip(text, 14) { HorizontalAlignment = HorizontalAlignment.Left };
        chip.BorderThickness = new Thickness(1);
        chip.Dyn(Border.BorderBrushProperty, "AccentBrush");
        chip.Click += click;
        return chip;
    }

    void Commit()
    {
        s.Save();
        changed();
    }

    // ---------- разделы ----------

    void BuildThemes()
    {
        var body = Section("Тема");
        var wrap = new WrapPanel();
        foreach (var t in ThemeManager.All)
        {
            var id = t.Id;
            var chip = new Chip(t.Name, 14);
            chip.Click += () =>
            {
                s.Theme = id;
                ThemeManager.Apply(id);
                foreach (var (c, cid) in themeChips) c.SetSelected(cid == id);
                Commit();
            };
            chip.SetSelected(s.Theme == id);
            themeChips.Add((chip, id));
            wrap.Children.Add(chip);
        }
        body.Children.Add(wrap);
        Check(body, "Режим стекла (прозрачные панели с размытием, как в iOS)", s.Glass, v => s.Glass = v);
    }

    void BuildBar()
    {
        var body = Section("Бар");
        Check(body, "Включить бар вместо панели задач", s.BarEnabled, v => s.BarEnabled = v);
        Check(body, "Скрыть панель задач Windows", s.HideTaskbar, v => s.HideTaskbar = v);

        var edgeRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        edgeRow.Children.Add(Ui.Text("Положение:  ", 14, "MutedBrush"));
        foreach (var (edge, name) in new[] { ("Top", "Сверху"), ("Bottom", "Снизу") })
        {
            var e = edge;
            var chip = new Chip(name, 14);
            chip.Click += () =>
            {
                s.BarEdge = e;
                foreach (var (c, ce) in edgeChips) c.SetSelected(ce == e);
                Commit();
            };
            chip.SetSelected(s.BarEdge == e);
            edgeChips.Add((chip, e));
            edgeRow.Children.Add(chip);
        }
        body.Children.Add(edgeRow);

        var heightRow = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        var heightLabel = Ui.Text($"Высота: {s.BarHeight} px", 14, "MutedBrush");
        heightLabel.Width = 120;
        var slider = new Slider
        {
            Minimum = 32, Maximum = 72, Value = s.BarHeight,
            TickFrequency = 2, IsSnapToTickEnabled = true,
        };
        slider.ValueChanged += (_, e) =>
        {
            s.BarHeight = (int)e.NewValue;
            heightLabel.Text = $"Высота: {s.BarHeight} px";
            if (IsLoaded) Commit();
        };
        DockPanel.SetDock(heightLabel, Dock.Left);
        heightRow.Children.Add(heightLabel);
        heightRow.Children.Add(slider);
        body.Children.Add(heightRow);

        // закреплённые программы
        var pinLabel = Ui.Text("Закреплённые программы", 14, "MutedBrush");
        pinLabel.Margin = new Thickness(0, 10, 0, 4);
        body.Children.Add(pinLabel);
        pinned.Dyn(Control.BackgroundProperty, "BgBrush");
        pinned.Dyn(Control.ForegroundProperty, "TextBrush");
        pinned.Dyn(Control.BorderBrushProperty, "CardBorderBrush");
        RefreshPinned();
        body.Children.Add(pinned);

        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Button("Добавить программу…", () =>
        {
            var dlg = new OpenFileDialog { Filter = "Программы и ярлыки|*.exe;*.lnk|Все файлы|*.*", Multiselect = true };
            if (dlg.ShowDialog(this) != true) return;
            foreach (var f in dlg.FileNames)
                if (!s.Pinned.Contains(f)) s.Pinned.Add(f);
            RefreshPinned();
            Commit();
        }));
        buttons.Children.Add(Button("Убрать выбранную", () =>
        {
            if (pinned.SelectedItem is string sel)
            {
                var path = s.Pinned.FirstOrDefault(p => System.IO.Path.GetFileName(p) == sel);
                if (path != null && !Settings.IsRequired(path)) s.Pinned.Remove(path);
                RefreshPinned();
                Commit();
            }
        }));
        body.Children.Add(buttons);
    }

    void RefreshPinned()
    {
        pinned.Items.Clear();
        foreach (var p in s.Pinned) pinned.Items.Add(System.IO.Path.GetFileName(p));
    }

    void BuildWidgets()
    {
        var body = Section("Виджеты на рабочем столе");
        Check(body, "Часы", s.ClockWidget, v => s.ClockWidget = v);
        Check(body, "Монитор ресурсов (процессор и память)", s.MonitorWidget, v => s.MonitorWidget = v);
        Check(body, "Заметки", s.NotesWidget, v => s.NotesWidget = v);
        foreach (var info in WidgetCatalog.All)
        {
            var id = info.Id;
            Check(body, info.Name, s.ExtraWidgets.Contains(id), v =>
            {
                if (v) { if (!s.ExtraWidgets.Contains(id)) s.ExtraWidgets.Add(id); }
                else s.ExtraWidgets.Remove(id);
            });
        }
        Field(body, "Город для погоды", s.City, v => s.City = v);
        Field(body, "Валюты (коды через запятую)", s.Currencies, v => s.Currencies = v);
        Field(body, "Репозиторий GitHub (владелец/имя)", s.GithubRepo, v => s.GithubRepo = v);
        Field(body, "ID сервера Discord (в настройках сервера должен быть включён виджет)", s.DiscordGuild, v => s.DiscordGuild = v);
        var hint = Ui.Text("Виджеты можно перетаскивать мышью — положение запоминается.", 12, "MutedBrush");
        hint.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(hint);
    }

    void BuildWallpaper()
    {
        var body = Section("Обои");
        Check(body, "Менять обои Windows на обои D4", s.WallpaperEnabled, v => s.WallpaperEnabled = v);
        Check(body, "Живые обои: частицы поверх картинки (пузыри, угольки, светлячки…)", s.AnimatedWallpaper, v => s.AnimatedWallpaper = v);

        var fpsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 2) };
        var fpsLabel = Ui.Text("Частота кадров анимации:", 14, "TextBrush");
        fpsLabel.VerticalAlignment = VerticalAlignment.Center;
        fpsLabel.Margin = new Thickness(0, 0, 10, 0);
        fpsRow.Children.Add(fpsLabel);
        var fpsBox = new ComboBox { Width = 110, FontSize = 14 };
        foreach (var f in new[] { 10, 15, 24, 30, 45, 60 }) fpsBox.Items.Add(f + " fps");
        int fpsIndex = Array.IndexOf(new[] { 10, 15, 24, 30, 45, 60 }, s.AnimFps);
        fpsBox.SelectedIndex = fpsIndex < 0 ? 2 : fpsIndex;
        fpsBox.SelectionChanged += (_, _) =>
        {
            var values = new[] { 10, 15, 24, 30, 45, 60 };
            if (fpsBox.SelectedIndex < 0) return;
            s.AnimFps = values[fpsBox.SelectedIndex];
            ParticleCanvas.Fps = s.AnimFps;
            Commit();
        };
        fpsRow.Children.Add(fpsBox);
        body.Children.Add(fpsRow);
        var fpsHint = Ui.Text("Меньше — легче для системы, особенно при включённом стекле.", 12, "MutedBrush");
        fpsHint.Margin = new Thickness(0, 0, 0, 4);
        body.Children.Add(fpsHint);

        var current = Ui.Text(string.IsNullOrWhiteSpace(s.WallpaperPath) ? "Сейчас: пейзаж темы" : "Сейчас: " + System.IO.Path.GetFileName(s.WallpaperPath), 13, "MutedBrush");
        current.Margin = new Thickness(0, 6, 0, 6);
        body.Children.Add(current);

        var row = new WrapPanel();
        row.Children.Add(Button("Выбрать картинку или видео…", () =>
        {
            var dlg = new OpenFileDialog { Filter = "Картинки и видео|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.mp4;*.wmv;*.avi;*.mov;*.m4v|Все файлы|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            s.WallpaperPath = dlg.FileName;
            current.Text = "Сейчас: " + System.IO.Path.GetFileName(dlg.FileName);
            Commit();
        }));
        row.Children.Add(Button("Вернуть пейзаж темы", () =>
        {
            s.WallpaperPath = null;
            current.Text = "Сейчас: пейзаж темы";
            Commit();
        }));
        body.Children.Add(row);

        var hint = Ui.Text("Картинки и пейзаж темы ставятся как обои Windows (при выходе возвращается прежняя картинка). Видео играет под иконками и встаёт на паузу под окнами на весь экран.", 12, "MutedBrush");
        hint.Margin = new Thickness(0, 6, 0, 0);
        hint.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(hint);
    }

    void BuildFolders()
    {
        var body = Section("Мини-папки");
        body.Children.Add(Button("Добавить мини-папку", () => addFolder()));
        var hint = Ui.Text("Перетащите файлы или программы в папку. Двойной щелчок по названию — переименовать, правый щелчок — форма и удаление.", 12, "MutedBrush");
        hint.Margin = new Thickness(0, 8, 0, 0);
        hint.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(hint);
    }

    void BuildSystem()
    {
        var body = Section("Система");
        bool auto = false;
        try
        {
            auto = Schtasks("/Query /TN D4") == 0;
        }
        catch { /* нет доступа к реестру */ }

        Check(body, "Красить системные окна Windows (Параметры, Проводник) в цвета темы", s.SyncWindowsTheme, v => s.SyncWindowsTheme = v);

        var cb = new CheckBox { Content = "Запускать вместе с Windows", IsChecked = auto, FontSize = 14, Margin = new Thickness(0, 4, 0, 4) };
        cb.Dyn(Control.ForegroundProperty, "TextBrush");
        cb.Checked += (_, _) => SetAutostart(true);
        cb.Unchecked += (_, _) => SetAutostart(false);
        body.Children.Add(cb);
    }

    /// <summary>Автозапуск через планировщик: так D4 стартует с правами администратора без окна UAC.</summary>
    static int Schtasks(string args)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("schtasks", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (p == null) return -1;
            p.StandardOutput.ReadToEnd();
            p.WaitForExit(8000);
            return p.ExitCode;
        }
        catch { return -1; }
    }

    static void SetAutostart(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(RunName, throwOnMissingValue: false); // старый способ автозапуска
        }
        catch { /* игнорируем */ }

        if (on)
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                Schtasks("/Create /TN D4 /TR \"\\\"" + exe + "\\\"\" /SC ONLOGON /RL HIGHEST /F");
        }
        else
        {
            Schtasks("/Delete /TN D4 /F");
        }
    }
}
