using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;

namespace D4;

public sealed class MiniFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Папка";
    /// <summary>Round, Capsule или Square.</summary>
    public string Shape { get; set; } = "Round";
    public List<string> Items { get; set; } = new();
}

public sealed class Settings
{
    public string Theme { get; set; } = "ocean";

    /// <summary>Режим стекла (прозрачные размытые панели).</summary>
    public bool Glass { get; set; }

    /// <summary>Версия формата настроек (нужна, чтобы перенести старые значения).</summary>
    public int SettingsVersion { get; set; }

    // Обои Windows до замены (чтобы вернуть при выходе)
    public bool OriginalSaved { get; set; }
    public string? OriginalWallpaper { get; set; }
    public string? OriginalStyle { get; set; }
    public string? OriginalTile { get; set; }

    // Тема системных окон Windows (до замены, чтобы вернуть при выходе)
    public bool SyncWindowsTheme { get; set; } = true;
    public bool WinThemeSaved { get; set; }
    public int? WinAppsLight { get; set; }
    public int? WinSystemLight { get; set; }
    public int? WinAccent { get; set; }
    public int? WinAccentMenu { get; set; }

    // Бар
    public bool BarEnabled { get; set; } = true;
    /// <summary>Top или Bottom.</summary>
    public string BarEdge { get; set; } = "Top";
    public int BarHeight { get; set; } = 44;
    public bool HideTaskbar { get; set; } = true;
    public List<string> Pinned { get; set; } = new();

    // Обои
    public bool WallpaperEnabled { get; set; } = true;
    /// <summary>Путь к картинке или видео. Пусто — сцена из текущей темы.</summary>
    public string? WallpaperPath { get; set; }

    // Виджеты
    public bool ClockWidget { get; set; } = true;
    public bool MonitorWidget { get; set; } = true;
    public bool NotesWidget { get; set; } = true;
    public string Notes { get; set; } = "";

    public List<MiniFolder> Folders { get; set; } = new();
    public Dictionary<string, double[]> Positions { get; set; } = new();

    // ---------- обязательные закрепления ----------

    static string EdgePath()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                 })
        {
            var p = Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(p)) return p;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe");
    }

    /// <summary>Приложения, которые всегда закреплены на баре: Проводник и Microsoft Edge.</summary>
    public static string[] RequiredPins() => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
        EdgePath(),
    };

    public static bool IsRequired(string? path) =>
        !string.IsNullOrEmpty(path) && RequiredPins().Any(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase));

    void EnsureRequired()
    {
        int at = 0;
        foreach (var req in RequiredPins())
        {
            int i = Pinned.FindIndex(p => string.Equals(p, req, StringComparison.OrdinalIgnoreCase));
            if (i < 0) Pinned.Insert(at, req);
            at++;
        }
    }

    // ---------- хранение ----------

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "D4");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    DispatcherTimer? saveTimer;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                if (loaded != null)
                {
                    // бар должен полностью заменять панель задач — для старых настроек включаем скрытие
                    if (loaded.SettingsVersion < 2) { loaded.HideTaskbar = true; loaded.SettingsVersion = 2; }
                    loaded.EnsureRequired();
                    return loaded;
                }
            }
        }
        catch { /* битый файл — начинаем с настроек по умолчанию */ }
        var fresh = new Settings { SettingsVersion = 2 };
        fresh.EnsureRequired();
        return fresh;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { /* диск недоступен — не падаем */ }
    }

    /// <summary>Отложенное сохранение: сливает частые изменения (перетаскивание, ввод текста) в одну запись.</summary>
    public void SaveSoon()
    {
        saveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        saveTimer.Tick -= OnSaveTick;
        saveTimer.Tick += OnSaveTick;
        saveTimer.Stop();
        saveTimer.Start();
    }

    void OnSaveTick(object? sender, EventArgs e)
    {
        saveTimer?.Stop();
        Save();
    }

    public static string DataDir => Dir;

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(Path.Combine(Dir, "d4.log"), DateTime.Now.ToString("s") + "  " + message + Environment.NewLine);
        }
        catch { /* лог не критичен */ }
    }
}
