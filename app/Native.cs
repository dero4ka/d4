using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace D4;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int left, top, right, bottom;
}

/// <summary>Вызовы Win32: AppBar, окно за иконками рабочего стола, список окон, нагрузка системы.</summary>
internal static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80;
    public const long WS_EX_APPWINDOW = 0x40000;
    public const long WS_EX_NOACTIVATE = 0x08000000;

    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    public static readonly IntPtr HWND_BOTTOM = new(1);

    public const int SM_CXSCREEN = 0, SM_CYSCREEN = 1, SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77;
    const uint GW_OWNER = 4;
    const int DWMWA_CLOAKED = 14;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr h, IntPtr rect, IntPtr rgn, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);

    // ---------- стили окон ----------

    public static void AddExStyle(IntPtr hwnd, long flags)
    {
        long v = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(v | flags));
    }

    public static void SendToBottom(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    // ---------- AppBar (бар резервирует место на рабочем столе) ----------

    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3;

    static APPBARDATA NewData(IntPtr hwnd) => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };

    public static void AppBarRegister(IntPtr hwnd)
    {
        var d = NewData(hwnd);
        SHAppBarMessage(ABM_NEW, ref d);
    }

    public static void AppBarRemove(IntPtr hwnd)
    {
        var d = NewData(hwnd);
        SHAppBarMessage(ABM_REMOVE, ref d);
    }

    /// <summary>Резервирует полосу толщиной <paramref name="thickness"/> пикселей у верхнего или нижнего края.</summary>
    public static RECT AppBarSetPos(IntPtr hwnd, bool top, int thickness)
    {
        var d = NewData(hwnd);
        d.uEdge = top ? 1u : 3u; // ABE_TOP / ABE_BOTTOM
        d.rc = new RECT { left = 0, top = 0, right = GetSystemMetrics(SM_CXSCREEN), bottom = GetSystemMetrics(SM_CYSCREEN) };
        SHAppBarMessage(ABM_QUERYPOS, ref d);
        if (top) d.rc.bottom = d.rc.top + thickness; else d.rc.top = d.rc.bottom - thickness;
        SHAppBarMessage(ABM_SETPOS, ref d);
        return d.rc;
    }

    // ---------- обычная панель задач Windows ----------

    public static void SetTaskbarVisible(bool visible)
    {
        int cmd = visible ? 5 : 0; // SW_SHOW / SW_HIDE
        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray != IntPtr.Zero) ShowWindow(tray, cmd);
        EnumWindows((h, _) =>
        {
            if (ClassName(h) == "Shell_SecondaryTrayWnd") ShowWindow(h, cmd);
            return true;
        }, IntPtr.Zero);
    }

    // ---------- окно за иконками рабочего стола ----------

    /// <summary>
    /// Находит окно, внутрь которого можно поместить обои, чтобы они лежали под иконками.
    /// Возвращает родителя и (для Windows 11 24H2+) окно, под которым нужно встать по Z-порядку.
    /// </summary>
    public static IntPtr GetWallpaperHost(out IntPtr insertAfter)
    {
        insertAfter = IntPtr.Zero;
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;

        // просим Explorer создать WorkerW для обоев
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0, 1000, out _);

        // Windows 10 / Windows 11 до 24H2: WorkerW рядом с окном, где лежат иконки
        IntPtr worker = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                worker = FindWindowEx(IntPtr.Zero, h, "WorkerW", null);
            return true;
        }, IntPtr.Zero);
        if (worker != IntPtr.Zero) return worker;

        // Windows 11 24H2+: WorkerW лежит внутри Progman — встаём в Progman под значками
        var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView != IntPtr.Zero)
        {
            insertAfter = defView;
            return progman;
        }
        return IntPtr.Zero;
    }

    public static void RedrawDesktop() =>
        RedrawWindow(GetDesktopWindow(), IntPtr.Zero, IntPtr.Zero, 0x1 | 0x4 | 0x80);

    // ---------- окна для бара ----------

    static string ClassName(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static List<(IntPtr Handle, string Title)> GetTaskWindows()
    {
        var list = new List<(IntPtr Handle, string Title)>();
        int self = Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return true;
            if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            int len = GetWindowTextLength(h);
            if (len == 0) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == (uint)self) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            var title = sb.ToString();
            if (title == "Program Manager") return true;
            list.Add((h, title));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static void Activate(IntPtr h)
    {
        if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
        SetForegroundWindow(h);
    }

    /// <summary>true, если на переднем плане окно на весь экран или развёрнутое — обои можно ставить на паузу.</summary>
    public static bool IsForegroundCovering()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        var cls = ClassName(h);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == (uint)Environment.ProcessId) return false;
        if (IsZoomed(h)) return true;
        if (!GetWindowRect(h, out var r)) return false;
        return r.left <= 0 && r.top <= 0 && r.right >= GetSystemMetrics(SM_CXSCREEN) && r.bottom >= GetSystemMetrics(SM_CYSCREEN);
    }

    // ---------- нагрузка системы ----------

    [StructLayout(LayoutKind.Sequential)]
    struct FILETIME
    {
        public uint lo, hi;
        public ulong Value => ((ulong)hi << 32) | lo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    static ulong prevIdle, prevKernel, prevUser;

    /// <summary>Загрузка процессора в процентах с прошлого вызова (первый вызов даёт неточное значение).</summary>
    public static double CpuUsage()
    {
        if (!GetSystemTimes(out var i, out var k, out var u)) return 0;
        ulong idle = i.Value, kernel = k.Value, user = u.Value;
        ulong dIdle = idle - prevIdle, dKernel = kernel - prevKernel, dUser = user - prevUser;
        prevIdle = idle; prevKernel = kernel; prevUser = user;
        ulong total = dKernel + dUser; // kernel уже включает idle
        return total == 0 ? 0 : Math.Clamp((double)(total - dIdle) / total * 100.0, 0, 100);
    }

    public static (double Percent, double UsedGb, double TotalGb) Memory()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) return (0, 0, 0);
        double total = m.ullTotalPhys / 1073741824.0;
        double used = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
        return (m.dwMemoryLoad, used, total);
    }
}
