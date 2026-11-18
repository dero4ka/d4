using System;
using System.Collections.Generic;
using System.IO;
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

    // Бар
    public bool BarEnabled { get; set; } = true;
    /// <summary>Top или Bottom.</summary>
    public string BarEdge { get; set; } = "Top";
    public int BarHeight { get; set; } = 44;
    public bool HideTaskbar { get; set; } = false;
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
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* битый файл — начинаем с настроек по умолчанию */ }
        return new Settings();
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
}
