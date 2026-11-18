using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace D4;

/// <summary>
/// Обои: картинка, видео или пейзаж текущей темы. Окно встаёт под иконки рабочего стола.
/// Видео ставится на паузу, когда поверх лежит окно на весь экран или развёрнутое окно.
/// </summary>
public sealed class WallpaperWindow : Window
{
    static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
    static readonly string[] VideoExt = { ".mp4", ".wmv", ".avi", ".mov", ".m4v" };

    readonly Settings s;
    readonly Grid root = new();
    readonly DispatcherTimer guard = new() { Interval = TimeSpan.FromSeconds(2) };
    MediaElement? media;
    bool pausedByGuard;

    public WallpaperWindow(Settings settings)
    {
        s = settings;
        Title = "D4 Wallpaper";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Content = root;

        Reload();

        ThemeManager.Changed += OnThemeChanged;
        guard.Tick += (_, _) => CheckGuard();
        guard.Start();
        Closed += (_, _) =>
        {
            ThemeManager.Changed -= OnThemeChanged;
            guard.Stop();
            media?.Stop();
            Native.RedrawDesktop();
        };
    }

    void OnThemeChanged()
    {
        if (string.IsNullOrWhiteSpace(s.WallpaperPath)) Reload();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);

        int w = Native.GetSystemMetrics(Native.SM_CXSCREEN);
        int h = Native.GetSystemMetrics(Native.SM_CYSCREEN);
        // координаты родителя начинаются с левого верхнего угла всех мониторов
        int offX = -Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
        int offY = -Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);

        var host = Native.GetWallpaperHost(out var insertAfter);
        if (host != IntPtr.Zero)
        {
            Native.SetParent(hwnd, host);
            Native.MoveWindow(hwnd, offX, offY, w, h, true);
            if (insertAfter != IntPtr.Zero)
                Native.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
        else
        {
            // запасной вариант: окно на самом дне, поверх обоев Windows, но под остальными окнами
            Native.MoveWindow(hwnd, 0, 0, w, h, true);
            Native.SendToBottom(hwnd);
        }
    }

    string? loadedPath;

    /// <summary>Перезагружает обои только если выбранный файл изменился (чтобы видео не перезапускалось от каждого клика в настройках).</summary>
    public void Refresh()
    {
        if (s.WallpaperPath != loadedPath) Reload();
    }

    /// <summary>Перерисовывает обои по текущим настройкам.</summary>
    public void Reload()
    {
        loadedPath = s.WallpaperPath;
        media?.Stop();
        media = null;
        root.Children.Clear();

        var path = s.WallpaperPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                if (Array.IndexOf(ImageExt, ext) >= 0)
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    bmp.Freeze();
                    root.Children.Add(new Image { Source = bmp, Stretch = Stretch.UniformToFill });
                    return;
                }
                if (Array.IndexOf(VideoExt, ext) >= 0)
                {
                    media = new MediaElement
                    {
                        LoadedBehavior = MediaState.Manual,
                        UnloadedBehavior = MediaState.Manual,
                        Stretch = Stretch.UniformToFill,
                        IsMuted = true,
                        Source = new Uri(path),
                    };
                    media.MediaEnded += (_, _) =>
                    {
                        media.Position = TimeSpan.Zero;
                        media.Play();
                    };
                    media.MediaFailed += (_, _) =>
                    {
                        // кодек не поддерживается — показываем пейзаж темы
                        root.Children.Clear();
                        root.Children.Add(SceneFactory.Build(ThemeManager.Current));
                    };
                    root.Children.Add(media);
                    media.Play();
                    return;
                }
            }
            catch
            {
                // не получилось открыть файл — ниже покажем пейзаж
            }
        }

        root.Children.Add(SceneFactory.Build(ThemeManager.Current));
    }

    void CheckGuard()
    {
        if (media == null) return;
        bool covered = Native.IsForegroundCovering();
        if (covered && !pausedByGuard)
        {
            media.Pause();
            pausedByGuard = true;
        }
        else if (!covered && pausedByGuard)
        {
            media.Play();
            pausedByGuard = false;
        }
    }
}
