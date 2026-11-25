using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Interop;

namespace D4;

/// <summary>Глобальные горячие клавиши (работают в любом приложении).</summary>
internal sealed class HotKeys : IDisposable
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

    const uint MOD_NOREPEAT = 0x4000;
    readonly HwndSource source;
    readonly Dictionary<int, Action> map = new();

    public HotKeys()
    {
        // окно-невидимка, которое только принимает сообщения (HWND_MESSAGE = -3)
        var p = new HwndSourceParameters("D4HotKeys") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 };
        source = new HwndSource(p);
        source.AddHook(Hook);
    }

    public void Add(int id, uint mods, uint vk, Action action)
    {
        map[id] = action;
        if (!RegisterHotKey(source.Handle, id, mods | MOD_NOREPEAT, vk))
            Settings.Log("hotkey " + id + ": сочетание занято другой программой");
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && map.TryGetValue(wParam.ToInt32(), out var action)) // WM_HOTKEY
        {
            handled = true;
            try { action(); } catch (Exception ex) { Settings.Log("hotkey action: " + ex.Message); }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in map.Keys) UnregisterHotKey(source.Handle, id);
        map.Clear();
        source.Dispose();
    }
}

/// <summary>Не даёт компьютеру и экрану засыпать, пока включено.</summary>
internal static class KeepAwake
{
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);

    const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1, ES_DISPLAY_REQUIRED = 0x2;

    public static bool On { get; private set; }

    public static void Set(bool on)
    {
        SetThreadExecutionState(on ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED : ES_CONTINUOUS);
        On = on;
    }

    public static void Toggle()
    {
        Set(!On);
        Shell.Notify(On ? "Компьютер не будет засыпать, пока включено" : "Обычный режим сна восстановлен");
    }
}

/// <summary>Перевод единиц в строке поиска: «5 км в мили», «100 f to c», «2 гб в мб».</summary>
internal static class UnitConvert
{
    static readonly Dictionary<string, (string Cat, double F)> Units = Build();

    static Dictionary<string, (string, double)> Build()
    {
        var d = new Dictionary<string, (string, double)>();
        void A(string cat, double f, params string[] names) { foreach (var n in names) d[n] = (cat, f); }
        A("len", 1, "м", "m", "метр", "метров", "метра", "meter", "meters");
        A("len", 1000, "км", "km", "километр", "километров", "километра");
        A("len", .01, "см", "cm");
        A("len", .001, "мм", "mm");
        A("len", 1609.344, "миля", "миль", "мили", "mile", "miles", "mi");
        A("len", .3048, "фут", "футов", "фута", "ft", "foot", "feet");
        A("len", .0254, "дюйм", "дюймов", "дюйма", "inch", "inches");
        A("len", .9144, "ярд", "ярдов", "yd");
        A("mass", 1, "кг", "kg");
        A("mass", .001, "г", "g", "гр");
        A("mass", 1e-6, "мг", "mg");
        A("mass", 1000, "т", "тонна", "тонн");
        A("mass", .45359237, "фунт", "фунтов", "lb", "lbs");
        A("mass", .028349523, "унция", "унций", "oz");
        A("vol", 1, "л", "l", "литр", "литров");
        A("vol", .001, "мл", "ml");
        A("vol", 3.785411784, "галлон", "галлонов", "gal");
        A("spd", 1, "м/с", "m/s");
        A("spd", 1 / 3.6, "км/ч", "km/h", "kmh", "кмч");
        A("spd", .44704, "mph", "миль/ч");
        A("data", 1, "б", "байт", "byte", "bytes");
        A("data", 1024, "кб", "kb");
        A("data", 1048576, "мб", "mb");
        A("data", 1073741824, "гб", "gb");
        A("data", 1099511627776, "тб", "tb");
        A("time", 1, "сек", "sec", "секунд");
        A("time", 60, "мин", "min", "минут");
        A("time", 3600, "ч", "h", "час", "часов", "часа");
        A("time", 86400, "день", "дней", "дня", "day", "days");
        // температура обрабатывается отдельно
        A("temp", 0, "°c", "c", "цельсий", "°f", "f", "фаренгейт", "k", "кельвин");
        return d;
    }

    static readonly Regex Pattern = new(@"^\s*(-?\d+(?:[.,]\d+)?)\s*([\p{L}°/]+)\s+(?:в|to|in|->|=>|→)\s+([\p{L}°/]+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static double ToKelvin(string u, double v) => u switch
    {
        "°f" or "f" or "фаренгейт" => (v - 32) * 5 / 9 + 273.15,
        "k" or "кельвин" => v,
        _ => v + 273.15,
    };

    static double FromKelvin(string u, double k) => u switch
    {
        "°f" or "f" or "фаренгейт" => (k - 273.15) * 9 / 5 + 32,
        "k" or "кельвин" => k,
        _ => k - 273.15,
    };

    /// <summary>Возвращает строку вида «3,1069 mi» или null, если это не запрос на перевод.</summary>
    public static string? TryConvert(string q)
    {
        var m = Pattern.Match(q);
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return null;
        string from = m.Groups[2].Value.ToLowerInvariant(), to = m.Groups[3].Value.ToLowerInvariant();
        if (!Units.TryGetValue(from, out var a) || !Units.TryGetValue(to, out var b) || a.Cat != b.Cat) return null;

        double result = a.Cat == "temp" ? FromKelvin(to, ToKelvin(from, v)) : v * a.F / b.F;
        var text = Math.Round(result, 6).ToString("0.######", CultureInfo.InvariantCulture).Replace('.', ',');
        return text + " " + m.Groups[3].Value;
    }
}
