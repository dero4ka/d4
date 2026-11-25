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
            ("wallpaper", BuildWallpaper), ("animations", BuildAnimations), ("folders", BuildFolders), ("system", BuildSystem),
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

    TextBox Field(StackPanel into, string label, string value, Action<string> set)
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
        return tb;
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
        Check(body, "Горячие углы: левый верхний — обзор окон, правый нижний — рабочий стол", s.HotCorners, v => s.HotCorners = v);

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

        var aiTitle = Ui.Text("ИИ-помощник (любой OpenAI-совместимый сервис)", 13, "AccentBrush", true);
        aiTitle.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(aiTitle);
        var tbEndpoint = Field(body, "Адрес API", s.AiEndpoint, v => s.AiEndpoint = v);
        var tbModel = Field(body, "Модель (пусто — по умолчанию у сервиса)", s.AiModel, v => s.AiModel = v);
        var tbKey = Field(body, "Ключ API (хранится в настройках открытым текстом; для Pollinations не нужен)", s.AiKey, v => s.AiKey = v);
        var presets = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        presets.Children.Add(Button("Pollinations (без ключа)", () =>
        {
            s.AiEndpoint = tbEndpoint.Text = "https://text.pollinations.ai/openai";
            s.AiModel = tbModel.Text = "";
            s.AiKey = tbKey.Text = "";
            Commit();
        }));
        presets.Children.Add(Button("OpenRouter (бесплатные модели, нужен ключ)", () =>
        {
            s.AiEndpoint = tbEndpoint.Text = "https://openrouter.ai/api/v1/chat/completions";
            s.AiModel = tbModel.Text = "openrouter/free";
            Commit();
        }));
        body.Children.Add(presets);
        var hint = Ui.Text("Виджеты можно перетаскивать мышью — положение запоминается.", 12, "MutedBrush");
        hint.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(hint);
    }

    /// <summary>Строка «подпись + выпадающий список».</summary>
    void ComboRow(StackPanel into, string label, string[] items, int index, Action<int> set)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 2) };
        var l = Ui.Text(label, 14, "TextBrush");
        l.VerticalAlignment = VerticalAlignment.Center;
        l.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(l);
        var box = new ComboBox { MinWidth = 120, FontSize = 14 };
        foreach (var it in items) box.Items.Add(it);
        box.SelectedIndex = index < 0 ? 0 : index;
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex < 0) return;
            set(box.SelectedIndex);
            Commit();
        };
        row.Children.Add(box);
        into.Children.Add(row);
    }

    void SubTitle(StackPanel into, string text)
    {
        var t = Ui.Text(text, 13, "AccentBrush", true);
        t.Margin = new Thickness(0, 14, 0, 2);
        into.Children.Add(t);
    }

    void BuildAnimations()
    {
        var body = Section("Анимации обоев");
        Check(body, "Живые обои: анимация поверх картинки", s.AnimatedWallpaper, v => s.AnimatedWallpaper = v);

        SubTitle(body, "Какие эффекты показывать");
        foreach (var (id, text) in new[]
        {
            ("particles", "Частицы: пузыри, искры, светлячки, боке"),
            ("weather", "Погода: дождь, снег, лепестки, туман, облака, молнии"),
            ("sky", "Небо: метеоры, северное сияние, галактика, лучи света"),
            ("water", "Вода: волны и круги"),
            ("special", "Особые: птицы, салют, лавовые пузыри, цифровой дождь, сетка"),
        })
        {
            var gid = id;
            Check(body, text, !s.AnimOff.Contains(gid), v =>
            {
                if (v) s.AnimOff.Remove(gid);
                else if (!s.AnimOff.Contains(gid)) s.AnimOff.Add(gid);
            });
        }

        SubTitle(body, "Насколько насыщенно");
        var densities = new[] { 50, 100, 160 };
        ComboRow(body, "Количество:", new[] { "Меньше", "Обычное", "Больше" }, Array.IndexOf(densities, s.AnimDensity), i => s.AnimDensity = densities[i]);
        var speeds = new[] { 50, 75, 100, 150, 200 };
        ComboRow(body, "Скорость:", new[] { "50%", "75%", "100%", "150%", "200%" }, Array.IndexOf(speeds, s.AnimSpeed), i => s.AnimSpeed = speeds[i]);
        var fps = new[] { 10, 15, 24, 30, 45, 60 };
        ComboRow(body, "Частота кадров:", fps.Select(f => f + " fps").ToArray(), Array.IndexOf(fps, s.AnimFps), i =>
        {
            s.AnimFps = fps[i];
            ParticleCanvas.Fps = s.AnimFps;
        });
        var hint = Ui.Text("Меньше кадров и частиц — легче для системы, особенно при включённом стекле.", 12, "MutedBrush");
        hint.Margin = new Thickness(0, 2, 0, 0);
        body.Children.Add(hint);

        SubTitle(body, "Когда ставить на паузу");
        Check(body, "Когда окно развёрнуто на весь экран", s.AnimPauseMaximized, v => s.AnimPauseMaximized = v);
        Check(body, "В полноэкранных приложениях и играх", s.AnimPauseFullscreen, v => s.AnimPauseFullscreen = v);
        Check(body, "Когда ноутбук работает от батареи", s.BatterySaver, v => { s.BatterySaver = v; ParticleCanvas.BatterySaver = v; });
        Check(body, "Когда процессор загружен больше, чем на:", s.AnimPauseCpu, v => s.AnimPauseCpu = v);
        var limits = new[] { 50, 70, 85, 95 };
        ComboRow(body, "Порог загрузки:", limits.Select(l => l + "%").ToArray(), Array.IndexOf(limits, s.AnimCpuLimit), i => s.AnimCpuLimit = limits[i]);

        SubTitle(body, "Когда включать");
        Check(body, "Анимировать только в заданные часы", s.AnimSchedule, v => s.AnimSchedule = v);
        Field(body, "С какого времени (ЧЧ:ММ)", s.AnimFrom, v => s.AnimFrom = v);
        Field(body, "До какого времени (ЧЧ:ММ; можно через полночь, например 20:00 — 08:00)", s.AnimTo, v => s.AnimTo = v);
    }

    void BuildWallpaper()
    {
        var body = Section("Обои");
        Check(body, "Менять обои Windows на обои D4", s.WallpaperEnabled, v => s.WallpaperEnabled = v);


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

        Check(body, "Горячие клавиши: Ctrl+Alt+Пробел — поиск, Ctrl+Alt+W — спрятать/показать виджеты", s.HotKeysEnabled, v => s.HotKeysEnabled = v);
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
