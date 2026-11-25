using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace D4;

/// <summary>Управляет всеми окнами D4: бар, обои, виджеты, мини-папки, настройки и значок в трее.</summary>
public sealed class Shell
{
    readonly Settings s;
    readonly Dictionary<string, MiniFolderWindow> folderWindows = new();

    BarWindow? bar;
    WinKeyHook? keyHook;
    HotKeys? hotKeys;
    bool widgetsHidden;
    readonly Dictionary<string, WidgetWindow> extras = new();
    readonly WallpaperService wallpaper;
    ClockWidget? clock;
    MonitorWidget? monitor;
    NotesWidget? notes;
    SettingsWindow? settingsWindow;
    System.Windows.Forms.NotifyIcon? tray;
    bool exiting;

    public Shell(Settings settings)
    {
        s = settings;
        wallpaper = new WallpaperService(settings);
    }

    public void Start()
    {
        ThemeManager.InitGlass(s.Glass);
        ThemeManager.Apply(s.Theme);
        BuildTray();
        Reapply();
    }

    static void Toggle<T>(ref T? window, bool on, Func<T> make) where T : Window
    {
        if (on && window == null)
        {
            window = make();
            window.Show();
        }
        else if (!on && window != null)
        {
            window.Close();
            window = null;
        }
    }

    /// <summary>Приводит открытые окна в соответствие с настройками.</summary>
    public void Reapply()
    {
        if (exiting) return;

        ThemeManager.SetGlass(s.Glass);
        wallpaper.Apply();

        // системные окна Windows — в цвета темы
        try
        {
            if (s.SyncWindowsTheme) SystemTheme.Apply(s, ThemeManager.Current);
            else SystemTheme.Restore(s);
        }
        catch (Exception ex) { Settings.Log("system theme: " + ex.Message); }

        // клавиша Win открывает меню D4, пока работает наш бар
        if (s.BarEnabled && keyHook == null) keyHook = new WinKeyHook(() => Launcher.Toggle(s));
        else if (!s.BarEnabled && keyHook != null)
        {
            keyHook.Dispose();
            keyHook = null;
        }

        // глобальные горячие клавиши
        if (s.HotKeysEnabled && hotKeys == null)
        {
            try
            {
                hotKeys = new HotKeys();
                hotKeys.Add(1, 0x3, 0x20, () => Launcher.Toggle(s)); // Ctrl+Alt+Пробел — поиск
                hotKeys.Add(2, 0x3, 0x57, ToggleWidgets);            // Ctrl+Alt+W — спрятать/показать виджеты
            }
            catch (Exception ex) { Settings.Log("hotkeys: " + ex.Message); }
        }
        else if (!s.HotKeysEnabled && hotKeys != null)
        {
            hotKeys.Dispose();
            hotKeys = null;
        }

        Toggle(ref clock, s.ClockWidget, () => new ClockWidget(s));
        Toggle(ref monitor, s.MonitorWidget, () => new MonitorWidget(s));
        Toggle(ref notes, s.NotesWidget, () => new NotesWidget(s));

        SyncExtras();
        SyncFolders();
        ApplyWidgetVisibility();

        Toggle(ref bar, s.BarEnabled, () => new BarWindow(s, OpenSettings, ToggleTaskbar, Exit, SetTheme));
        bar?.ApplySettings();

        Native.SetTaskbarVisible(!(s.BarEnabled && s.HideTaskbar));
    }

    IEnumerable<WidgetWindow> AllWidgets()
    {
        if (clock != null) yield return clock;
        if (monitor != null) yield return monitor;
        if (notes != null) yield return notes;
        foreach (var w in extras.Values) yield return w;
        foreach (var w in folderWindows.Values) yield return w;
    }

    /// <summary>Прячет все виджеты и мини-папки с рабочего стола или возвращает их.</summary>
    public void ToggleWidgets()
    {
        widgetsHidden = !widgetsHidden;
        ApplyWidgetVisibility();
        Notify(widgetsHidden ? "Виджеты спрятаны. Вернуть: Ctrl+Alt+W" : "Виджеты на месте");
    }

    void ApplyWidgetVisibility()
    {
        foreach (var w in AllWidgets().ToList())
        {
            try
            {
                if (widgetsHidden) { if (w.IsVisible) w.Hide(); }
                else if (!w.IsVisible) { w.Show(); w.SendBack(); }
            }
            catch (Exception ex) { Settings.Log("widget visibility: " + ex.Message); }
        }
    }

    /// <summary>Включает и выключает дополнительные виджеты (погода, музыка, календарь и др.).</summary>
    void SyncExtras()
    {
        foreach (var info in WidgetCatalog.All)
        {
            bool on = s.ExtraWidgets.Contains(info.Id);
            if (on && !extras.ContainsKey(info.Id))
            {
                try
                {
                    var w = info.Make(s);
                    extras[info.Id] = w;
                    w.Show();
                }
                catch (Exception ex)
                {
                    Settings.Log("widget " + info.Id + ": " + ex);
                }
            }
            else if (!on && extras.TryGetValue(info.Id, out var old))
            {
                old.Close();
                extras.Remove(info.Id);
            }
            else if (on && extras.TryGetValue(info.Id, out var cur))
            {
                cur.OnSettingsChanged();
            }
        }
    }

    void SyncFolders()
    {
        var ids = s.Folders.Select(f => f.Id).ToHashSet();

        foreach (var id in folderWindows.Keys.Where(k => !ids.Contains(k)).ToList())
        {
            folderWindows[id].Close();
            folderWindows.Remove(id);
        }

        foreach (var f in s.Folders)
        {
            if (folderWindows.ContainsKey(f.Id)) continue;
            var w = new MiniFolderWindow(s, f, DeleteFolder);
            folderWindows[f.Id] = w;
            w.Show();
        }
    }

    public void AddFolder()
    {
        int n = s.Folders.Count;
        var folder = new MiniFolder { Title = "Папка " + (n + 1) };
        s.Positions["folder:" + folder.Id] = new[] { 340.0 + n * 30, 240.0 + n * 30 };
        s.Folders.Add(folder);
        s.Save();
        SyncFolders();
    }

    void DeleteFolder(MiniFolder folder)
    {
        s.Folders.Remove(folder);
        s.Positions.Remove("folder:" + folder.Id);
        s.Save();
        SyncFolders();
    }

    public void OpenSettings()
    {
        try
        {
            if (settingsWindow != null)
            {
                if (!settingsWindow.IsLoaded)
                {
                    settingsWindow = null; // окно умерло, не успев закрыться: откроем заново
                }
                else
                {
                    if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
                    settingsWindow.Show();
                    settingsWindow.Topmost = true; // выводим поверх остальных, потом возвращаем
                    settingsWindow.Topmost = false;
                    settingsWindow.Activate();
                    return;
                }
            }
            var w = new SettingsWindow(s, Reapply, AddFolder);
            settingsWindow = w;
            w.Closed += (_, _) => { if (ReferenceEquals(settingsWindow, w)) settingsWindow = null; };
            w.Show();
            w.Activate();
        }
        catch (Exception ex)
        {
            Settings.Log("settings open: " + ex);
            settingsWindow = null;
        }
    }

    /// <summary>Смена темы на лету: без перезапуска и без пересборки окон.</summary>
    void SetTheme(string id)
    {
        s.Theme = id;
        ThemeManager.Apply(id);
        s.Save();
        // обои-сцена зависят от темы
        try { Reapply(); } catch (Exception ex) { Settings.Log("reapply after theme: " + ex.Message); }
    }

    void ToggleTaskbar()
    {
        s.HideTaskbar = !s.HideTaskbar;
        s.Save();
        Reapply();
    }

    /// <summary>Значок D4 из самого exe; если не получилось — стандартный.</summary>
    static System.Drawing.Icon AppIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (path != null && System.IO.Path.GetFileName(path).Equals("D4.exe", StringComparison.OrdinalIgnoreCase))
            {
                var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (ico != null) return ico;
            }
        }
        catch { /* возьмём стандартный */ }
        return System.Drawing.SystemIcons.Application;
    }

    public static Shell? Instance { get; private set; }

    /// <summary>Всплывающее уведомление у значка в трее.</summary>
    public static void Notify(string text)
    {
        try { Instance?.tray?.ShowBalloonTip(4000, "D4", text, System.Windows.Forms.ToolTipIcon.Info); }
        catch { /* уведомление не критично */ }
    }

    void BuildTray()
    {
        Instance = this;
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Настройки", null, (_, _) => OpenSettings());
        menu.Items.Add("Скрыть/показать панель задач Windows", null, (_, _) => ToggleTaskbar());
        menu.Items.Add("Спрятать/показать виджеты (Ctrl+Alt+W)", null, (_, _) => ToggleWidgets());
        var awake = new System.Windows.Forms.ToolStripMenuItem("Не давать компьютеру засыпать");
        awake.Click += (_, _) =>
        {
            KeepAwake.Toggle();
            awake.Checked = KeepAwake.On;
        };
        menu.Items.Add(awake);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Exit());

        tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = AppIcon(),
            Text = "D4",
            Visible = true,
            ContextMenuStrip = menu,
        };
        tray.DoubleClick += (_, _) => OpenSettings();
    }

    /// <summary>Возвращает панель задач Windows — вызывается и при обычном выходе, и при сбое.</summary>
    public static void RestoreTaskbar() => Native.SetTaskbarVisible(true);

    public void Exit()
    {
        if (exiting) return;
        exiting = true;

        if (tray != null)
        {
            tray.Visible = false;
            tray.Dispose();
            tray = null;
        }

        keyHook?.Dispose();
        keyHook = null;
        hotKeys?.Dispose();
        hotKeys = null;
        KeepAwake.Set(false);
        RestoreTaskbar();
        try { SystemTheme.Restore(s); } catch (Exception ex) { Settings.Log("system theme restore: " + ex.Message); }
        s.Save();

        bar?.Close();
        clock?.Close();
        monitor?.Close();
        notes?.Close();
        foreach (var w in folderWindows.Values.ToList()) w.Close();
        foreach (var w in extras.Values.ToList()) w.Close();
        settingsWindow?.Close();
        wallpaper.Shutdown();

        Application.Current.Shutdown();
    }
}
