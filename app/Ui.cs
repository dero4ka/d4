using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace D4;

internal static class Ui
{
    public static TextBlock Text(string text, double size = 14, string brush = "TextBrush", bool bold = false)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    /// <summary>Привязывает свойство к ресурсу темы, чтобы оно менялось вместе с темой.</summary>
    public static T Dyn<T>(this T el, DependencyProperty p, string key) where T : FrameworkElement
    {
        el.SetResourceReference(p, key);
        return el;
    }
}

/// <summary>Кнопка-«таблетка» в стиле темы.</summary>
public sealed class Chip : Border
{
    public event Action? Click;
    public TextBlock Label { get; }

    public Chip(string text, double size = 14)
    {
        Label = Ui.Text(text, size);
        Label.Margin = new Thickness(10, 5, 10, 5);
        Child = Label;
        CornerRadius = new CornerRadius(10);
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(2);
        BorderBrush = Brushes.Transparent;
        Cursor = Cursors.Hand;
        Margin = new Thickness(2);

        MouseEnter += (_, _) => this.Dyn(BackgroundProperty, "HoverBrush");
        MouseLeave += (_, _) => Background = Brushes.Transparent;
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true; // не проваливаться в родителя (например, крестик внутри строки списка)
            Click?.Invoke();
        };
    }

    public void SetSelected(bool selected)
    {
        if (selected) this.Dyn(BorderBrushProperty, "AccentBrush");
        else BorderBrush = Brushes.Transparent;
    }
}

/// <summary>Запуск файлов и иконки для ярлыков.</summary>
internal static class AppUtil
{
    static readonly Dictionary<string, ImageSource?> IconCache = new();

    static readonly bool Elevated = IsElevated();

    static bool IsElevated()
    {
        try { return new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }

    public static void Launch(string path)
    {
        // D4 работает от администратора, а приложения, которые он открывает, не должны получать лишние права —
        // запуск через Проводник возвращает им обычные права пользователя
        if (Elevated)
        {
            try
            {
                var arg = path.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase) ? "" : "\"" + path + "\"";
                Process.Start(new ProcessStartInfo("explorer.exe", arg) { UseShellExecute = false });
                return;
            }
            catch { /* запустим напрямую */ }
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // файл удалён или нет доступа — молча игнорируем
        }
    }

    /// <summary>Значок, который окно само сообщает системе (нужен для приложений из Store и окон без своего exe).</summary>
    public static ImageSource? GetWindowIcon(IntPtr hwnd)
    {
        try
        {
            var h = Native.GetWindowIcon(hwnd);
            if (h == IntPtr.Zero) return null;
            var bmp = Imaging.CreateBitmapSourceFromHIcon(h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    static ImageSource? defaultIcon;

    public static ImageSource? DefaultIcon()
    {
        if (defaultIcon != null) return defaultIcon;
        try
        {
            var bmp = Imaging.CreateBitmapSourceFromHIcon(System.Drawing.SystemIcons.Application.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            defaultIcon = bmp;
        }
        catch { /* без запасного значка */ }
        return defaultIcon;
    }

    /// <summary>Путь, на который указывает ярлык .lnk (или null).</summary>
    public static string? ResolveShortcut(string lnk)
    {
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return null;
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic sc = shell.CreateShortcut(lnk);
            string target = sc.TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch { return null; }
    }

    public static ImageSource? GetIcon(string path)
    {
        if (IconCache.TryGetValue(path, out var cached)) return cached;
        ImageSource? result = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon != null)
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                result = bmp;
            }
        }
        catch
        {
            // например, это папка — вернём null, вызывающий покажет запасной значок
        }
        IconCache[path] = result;
        return result;
    }
}
