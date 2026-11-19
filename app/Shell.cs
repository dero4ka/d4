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

        wallpaper.Apply();

        Toggle(ref clock, s.ClockWidget, () => new ClockWidget(s));
        Toggle(ref monitor, s.MonitorWidget, () => new MonitorWidget(s));
        Toggle(ref notes, s.NotesWidget, () => new NotesWidget(s));

        SyncFolders();

        Toggle(ref bar, s.BarEnabled, () => new BarWindow(s, OpenSettings, ToggleTaskbar, Exit, SetTheme));
        bar?.ApplySettings();

        Native.SetTaskbarVisible(!(s.BarEnabled && s.HideTaskbar));
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
        if (settingsWindow != null)
        {
            settingsWindow.Activate();
            return;
        }
        settingsWindow = new SettingsWindow(s, Reapply, AddFolder);
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
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

    void BuildTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Настройки", null, (_, _) => OpenSettings());
        menu.Items.Add("Скрыть/показать панель задач Windows", null, (_, _) => ToggleTaskbar());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Exit());

        tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
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

        RestoreTaskbar();
        s.Save();

        bar?.Close();
        clock?.Close();
        monitor?.Close();
        notes?.Close();
        foreach (var w in folderWindows.Values.ToList()) w.Close();
        settingsWindow?.Close();
        wallpaper.Shutdown();

        Application.Current.Shutdown();
    }
}
