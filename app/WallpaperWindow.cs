using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace D4;

/// <summary>
/// Живые обои: окно с видео, встроенное под иконки рабочего стола.
/// Видео ставится на паузу, когда поверх лежит окно на весь экран или развёрнутое окно.
/// </summary>
public sealed class WallpaperWindow : Window
{
    readonly DispatcherTimer guard = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly MediaElement media;
    bool paused;

    public string VideoPath { get; }

    public WallpaperWindow(string videoPath)
    {
        VideoPath = videoPath;

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

        media = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Stretch = Stretch.UniformToFill,
            IsMuted = true,
            Source = new Uri(videoPath),
        };
        media.MediaEnded += (_, _) =>
        {
            media.Position = TimeSpan.Zero;
            media.Play();
        };
        media.MediaFailed += (_, e) =>
        {
            Settings.Log("видео не открылось: " + e.ErrorException?.Message);
            Close(); // кодек не поддерживается — остаются обои Windows с пейзажем темы
        };
        Content = media;

        Loaded += (_, _) => media.Play();
        guard.Tick += (_, _) => CheckGuard();
        guard.Start();
        Closed += (_, _) =>
        {
            guard.Stop();
            media.Stop();
            Native.RedrawDesktop();
        };
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
            Native.MakeChild(hwnd);
            Native.MoveWindow(hwnd, offX, offY, w, h, true);
            if (insertAfter != IntPtr.Zero)
                Native.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
        else
        {
            // запасной вариант: окно на самом дне, под остальными окнами
            Native.MoveWindow(hwnd, 0, 0, w, h, true);
            Native.SendToBottom(hwnd);
        }
        Settings.Log("видео-обои: " + Native.LastHostMode);
    }

    void CheckGuard()
    {
        bool covered = Native.IsForegroundCovering();
        if (covered && !paused)
        {
            media.Pause();
            paused = true;
        }
        else if (!covered && paused)
        {
            media.Play();
            paused = false;
        }
    }
}
