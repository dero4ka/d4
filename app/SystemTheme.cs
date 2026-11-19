using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;

namespace D4;

/// <summary>
/// Подстраивает системные окна Windows (Параметры, Проводник и другие приложения) под тему D4:
/// светлый или тёмный режим и цвет акцента. Прежние значения сохраняются и возвращаются при выходе.
/// </summary>
public static class SystemTheme
{
    const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string Dwm = @"Software\Microsoft\Windows\DWM";
    const string Accent = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);

    static string lastApplied = "";

    static int? Read(string path, string name)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(path);
            return k?.GetValue(name) is int v ? v : null;
        }
        catch { return null; }
    }

    static void Write(string path, string name, int? value)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(path);
            if (k == null) return;
            if (value == null) k.DeleteValue(name, false);
            else k.SetValue(name, value.Value, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            Settings.Log("system theme write " + name + ": " + ex.Message);
        }
    }

    static void Broadcast()
    {
        SendMessageTimeout(new IntPtr(0xFFFF), 0x1A, IntPtr.Zero, "ImmersiveColorSet", 2, 300, out _);
    }

    /// <summary>Windows хранит цвет как ABGR.</summary>
    static int Abgr(Color c) => unchecked((int)(0xFF000000u | ((uint)c.B << 16) | ((uint)c.G << 8) | c.R));

    public static void Apply(Settings s, Theme t)
    {
        if (lastApplied == t.Id) return;

        if (!s.WinThemeSaved)
        {
            s.WinAppsLight = Read(Personalize, "AppsUseLightTheme");
            s.WinSystemLight = Read(Personalize, "SystemUsesLightTheme");
            s.WinAccent = Read(Dwm, "AccentColor");
            s.WinAccentMenu = Read(Accent, "AccentColorMenu");
            s.WinThemeSaved = true;
            s.Save();
        }

        int light = t.Dark ? 0 : 1;
        Write(Personalize, "AppsUseLightTheme", light);
        Write(Personalize, "SystemUsesLightTheme", light);

        int accent = Abgr(ThemeManager.C(t.Accent));
        Write(Dwm, "AccentColor", accent);
        Write(Accent, "AccentColorMenu", accent);

        lastApplied = t.Id;
        Broadcast();
    }

    public static void Restore(Settings s)
    {
        if (!s.WinThemeSaved) return;
        Write(Personalize, "AppsUseLightTheme", s.WinAppsLight);
        Write(Personalize, "SystemUsesLightTheme", s.WinSystemLight);
        Write(Dwm, "AccentColor", s.WinAccent);
        Write(Accent, "AccentColorMenu", s.WinAccentMenu);
        s.WinThemeSaved = false;
        lastApplied = "";
        s.Save();
        Broadcast();
    }
}
