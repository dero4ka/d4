using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace D4;

/// <summary>
/// Обои. Картинки и пейзаж темы ставятся как настоящие обои Windows — это работает на любой версии системы.
/// Видео играет в отдельном окне под иконками рабочего стола. Прежние обои возвращаются при выходе.
/// </summary>
public sealed class WallpaperService
{
    static readonly string[] VideoExt = { ".mp4", ".wmv", ".avi", ".mov", ".m4v" };
    static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

    readonly Settings s;
    WallpaperWindow? video;
    ParticleWindow? particles;
    string? appliedKey;

    public WallpaperService(Settings settings) => s = settings;

    public void Apply()
    {
        try
        {
            if (!s.WallpaperEnabled)
            {
                CloseVideo();
                CloseParticles();
                Restore();
                return;
            }

            var path = s.WallpaperPath;
            bool exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            var ext = exists ? Path.GetExtension(path!).ToLowerInvariant() : "";

            if (exists && Array.IndexOf(VideoExt, ext) >= 0)
            {
                ApplyStatic(null);   // под видео — пейзаж темы на случай, если окно не встроится
                EnsureVideo(path!);
                CloseParticles();
            }
            else
            {
                CloseVideo();
                ApplyStatic(exists && Array.IndexOf(ImageExt, ext) >= 0 ? path : null);
                if (s.AnimatedWallpaper) EnsureParticles();
                else CloseParticles();
            }
        }
        catch (Exception ex)
        {
            Settings.Log("wallpaper: " + ex);
        }
    }

    public void Shutdown()
    {
        CloseVideo();
        CloseParticles();
        Restore();
    }

    // ---------- частицы ----------

    void EnsureParticles()
    {
        if (particles != null) return;
        var w = new ParticleWindow();
        w.Closed += (sender, _) =>
        {
            if (ReferenceEquals(particles, sender)) particles = null;
        };
        particles = w;
        w.Show();
    }

    void CloseParticles()
    {
        var p = particles;
        particles = null;
        p?.Close();
    }

    // ---------- видео ----------

    void EnsureVideo(string path)
    {
        if (video != null && video.VideoPath == path) return;
        CloseVideo();
        var w = new WallpaperWindow(path);
        w.Closed += (sender, _) =>
        {
            if (ReferenceEquals(video, sender)) video = null;
        };
        video = w;
        w.Show();
    }

    void CloseVideo()
    {
        var v = video;
        video = null;
        v?.Close();
    }

    // ---------- картинки и пейзаж ----------

    void ApplyStatic(string? imagePath)
    {
        double sw = Native.GetSystemMetrics(Native.SM_CXSCREEN);
        double sh = Native.GetSystemMetrics(Native.SM_CYSCREEN);
        double scale = Math.Min(1.0, 3840.0 / sw);
        int w = Math.Max(640, (int)(sw * scale));
        int h = Math.Max(360, (int)(sh * scale));

        var theme = ThemeManager.Current;
        string key = imagePath != null
            ? "img:" + imagePath + ":" + File.GetLastWriteTimeUtc(imagePath).Ticks
            : "scene:" + theme.Id + ":" + w + "x" + h;
        if (key == appliedKey) return;

        SaveOriginalOnce();

        string file = imagePath != null ? PrepareImage(imagePath) : RenderScene(theme, w, h);
        WriteDesk("WallpaperStyle", "10"); // «Заполнение»
        WriteDesk("TileWallpaper", "0");
        Native.SetSystemWallpaper(file);
        appliedKey = key;
    }

    static string PrepareImage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext != ".gif") return path;

        // Windows не принимает gif как обои — сохраняем первый кадр как png
        Directory.CreateDirectory(Settings.DataDir);
        var png = Path.Combine(Settings.DataDir, "wallpaper-" + Path.GetFileNameWithoutExtension(path) + ".png");
        var frame = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(frame);
        using var fs = File.Create(png);
        enc.Save(fs);
        return png;
    }

    static string RenderScene(Theme t, int w, int h)
    {
        Directory.CreateDirectory(Settings.DataDir);
        var file = Path.Combine(Settings.DataDir, $"scene-{t.Id}-v3-{w}x{h}.png");
        if (File.Exists(file)) return file; // тот же пейзаж того же размера уже нарисован

        var host = new Border { Width = w, Height = h, Child = SceneFactory.BuildWallpaper(t, w, h) };
        host.Measure(new Size(w, h));
        host.Arrange(new Rect(0, 0, w, h));
        host.UpdateLayout();

        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(host);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
        return file;
    }

    // ---------- возврат прежних обоев ----------

    void SaveOriginalOnce()
    {
        if (s.OriginalSaved) return;
        s.OriginalWallpaper = Native.GetSystemWallpaper();
        s.OriginalStyle = ReadDesk("WallpaperStyle");
        s.OriginalTile = ReadDesk("TileWallpaper");
        s.OriginalSaved = true;
        s.Save();
    }

    void Restore()
    {
        if (!s.OriginalSaved) return;
        if (s.OriginalStyle != null) WriteDesk("WallpaperStyle", s.OriginalStyle);
        if (s.OriginalTile != null) WriteDesk("TileWallpaper", s.OriginalTile);
        Native.SetSystemWallpaper(s.OriginalWallpaper ?? "");
        s.OriginalSaved = false;
        s.OriginalWallpaper = null;
        s.OriginalStyle = null;
        s.OriginalTile = null;
        appliedKey = null;
        s.Save();
    }

    static string? ReadDesk(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            return key?.GetValue(name) as string;
        }
        catch { return null; }
    }

    static void WriteDesk(string name, string value)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
            key?.SetValue(name, value, RegistryValueKind.String);
        }
        catch { /* нет доступа к реестру */ }
    }
}
