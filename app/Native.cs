using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace D4;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int left, top, right, bottom;
}

/// <summary>Окно приложения для бара: дескриптор, заголовок, путь к exe.</summary>
public sealed record WinInfo(IntPtr Handle, string Title, string? ExePath, bool Uwp);

/// <summary>Вызовы Win32: AppBar, окно за иконками рабочего стола, список окон, обои, нагрузка системы.</summary>
internal static class Native
{
    public const int GWL_STYLE = -16;
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
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc proc, IntPtr lParam);
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
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern IntPtr GetClassLongPtr(IntPtr h, int index);
    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr h, IntPtr rect, IntPtr rgn, uint flags);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")] static extern bool SpiGet(uint action, uint param, StringBuilder value, uint winIni);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")] static extern bool SpiSet(uint action, uint param, string value, uint winIni);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder name, ref int size);

    // ---------- стили окон ----------

    public static void AddExStyle(IntPtr hwnd, long flags)
    {
        long v = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(v | flags));
    }

    /// <summary>Превращает окно в дочернее (нужно, чтобы оно жило внутри окна рабочего стола).</summary>
    public static void MakeChild(IntPtr hwnd)
    {
        long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64() & 0xFFFFFFFFL;
        style &= ~0x80000000L;          // WS_POPUP
        style |= 0x40000000L | 0x10000000L; // WS_CHILD | WS_VISIBLE
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));
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

    const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_GETSTATE = 4, ABM_SETSTATE = 10;

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

    static uint origTaskbarState;
    static bool taskbarHiddenByUs;
    static bool watcherStarted;

    static string TaskbarMarker => Path.Combine(Settings.DataDir, "taskbar.state");

    public static bool IsTaskbarVisible()
    {
        var t = FindWindow("Shell_TrayWnd", null);
        return t != IntPtr.Zero && IsWindowVisible(t);
    }

    /// <summary>
    /// Прячет панель задач или возвращает её. При скрытии дополнительно включается автоскрытие,
    /// чтобы Windows освободила резерв рабочей области и окна не оставляли пустую полосу.
    /// </summary>
    public static void SetTaskbarVisible(bool visible)
    {
        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return;

        if (!visible)
        {
            if (!taskbarHiddenByUs)
            {
                var g = NewData(tray);
                origTaskbarState = (uint)SHAppBarMessage(ABM_GETSTATE, ref g).ToUInt32();
                taskbarHiddenByUs = true;
                SaveTaskbarMarker();
                StartTaskbarWatcher();
            }
            var d = NewData(tray);
            d.lParam = new IntPtr((int)(origTaskbarState | 1)); // ABS_AUTOHIDE
            SHAppBarMessage(ABM_SETSTATE, ref d);
        }
        else if (taskbarHiddenByUs)
        {
            var d = NewData(tray);
            d.lParam = new IntPtr((int)origTaskbarState);
            SHAppBarMessage(ABM_SETSTATE, ref d);
            taskbarHiddenByUs = false;
            DeleteTaskbarMarker();
        }

        int cmd = visible ? 5 : 0; // SW_SHOW / SW_HIDE
        ShowWindow(tray, cmd);
        EnumWindows((h, _) =>
        {
            if (ClassName(h) == "Shell_SecondaryTrayWnd") ShowWindow(h, cmd);
            return true;
        }, IntPtr.Zero);
    }

    static void SaveTaskbarMarker()
    {
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            File.WriteAllText(TaskbarMarker, origTaskbarState.ToString());
        }
        catch { /* запись не критична */ }
    }

    static void DeleteTaskbarMarker()
    {
        try { File.Delete(TaskbarMarker); } catch { /* нечего удалять */ }
    }

    /// <summary>
    /// Запускает крошечный процесс-сторож: если D4 закроют через диспетчер задач или он упадёт,
    /// сторож дождётся его завершения и вернёт панель Windows.
    /// </summary>
    static void StartTaskbarWatcher()
    {
        if (watcherStarted) return;
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || string.Equals(Path.GetFileName(exe), "dotnet.exe", StringComparison.OrdinalIgnoreCase)) return;
            Process.Start(new ProcessStartInfo(exe, "--watch " + Environment.ProcessId) { UseShellExecute = false, CreateNoWindow = true });
            watcherStarted = true;
        }
        catch (Exception ex)
        {
            Settings.Log("сторож панели не запустился: " + ex.Message);
        }
    }

    /// <summary>Возвращает панель задач по записи на диске: после сбоя, принудительного закрытия или из сторожа.</summary>
    public static void RecoverTaskbar()
    {
        try
        {
            if (!File.Exists(TaskbarMarker)) return;
            uint state = uint.TryParse(File.ReadAllText(TaskbarMarker).Trim(), out var v) ? v : 0;
            var tray = FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero)
            {
                var d = NewData(tray);
                d.lParam = new IntPtr((int)state);
                SHAppBarMessage(ABM_SETSTATE, ref d);
                ShowWindow(tray, 5);
                EnumWindows((h, _) =>
                {
                    if (ClassName(h) == "Shell_SecondaryTrayWnd") ShowWindow(h, 5);
                    return true;
                }, IntPtr.Zero);
            }
            File.Delete(TaskbarMarker);
        }
        catch { /* ничего не поделать */ }
    }

    // ---------- окно за иконками рабочего стола ----------

    /// <summary>Как удалось найти окно для живых обоев (пишется в лог).</summary>
    public static string LastHostMode = "не искали";

    /// <summary>
    /// Находит окно, внутрь которого можно поместить обои, чтобы они лежали под иконками.
    /// Возвращает родителя и (для Windows 11 24H2+) окно, под которым нужно встать по Z-порядку.
    /// </summary>
    public static IntPtr GetWallpaperHost(out IntPtr insertAfter)
    {
        insertAfter = IntPtr.Zero;
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            LastHostMode = "Progman не найден";
            return IntPtr.Zero;
        }

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
        if (worker != IntPtr.Zero)
        {
            LastHostMode = "WorkerW";
            return worker;
        }

        // Windows 11 24H2+: встаём в Progman под значками
        var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView != IntPtr.Zero)
        {
            insertAfter = defView;
            LastHostMode = "Progman (24H2)";
            return progman;
        }

        LastHostMode = "окно рабочего стола не найдено";
        return IntPtr.Zero;
    }

    public static void RedrawDesktop() =>
        RedrawWindow(GetDesktopWindow(), IntPtr.Zero, IntPtr.Zero, 0x1 | 0x4 | 0x80);

    // ---------- обои Windows ----------

    public static string GetSystemWallpaper()
    {
        var sb = new StringBuilder(520);
        SpiGet(0x73, (uint)sb.Capacity, sb, 0); // SPI_GETDESKWALLPAPER
        return sb.ToString();
    }

    public static void SetSystemWallpaper(string path) =>
        SpiSet(0x14, 0, path, 0x1 | 0x2); // SPI_SETDESKWALLPAPER, UPDATEINIFILE | SENDCHANGE

    // ---------- окна для бара ----------

    static string ClassName(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    static string? ExeOfPid(uint pid)
    {
        var h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    /// <summary>Путь к exe, которому принадлежит окно. Для окон приложений из Store ищет настоящий процесс.</summary>
    public static string? ExePath(IntPtr hwnd, out bool uwp)
    {
        uwp = false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        var path = ExeOfPid(pid);
        if (path != null && string.Equals(Path.GetFileName(path), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            uwp = true;
            string? real = null;
            EnumChildWindows(hwnd, (c, _) =>
            {
                GetWindowThreadProcessId(c, out uint cp);
                if (cp != pid)
                {
                    var rp = ExeOfPid(cp);
                    if (rp != null)
                    {
                        real = rp;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
            if (real != null) path = real;
        }
        return path;
    }

    public static List<WinInfo> GetTaskWindows()
    {
        var list = new List<WinInfo>();
        uint self = (uint)Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return true;
            if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            int len = GetWindowTextLength(h);
            if (len == 0) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == self) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            var title = sb.ToString();
            if (title == "Program Manager") return true;
            var exe = ExePath(h, out bool uwp);
            list.Add(new WinInfo(h, title, exe, uwp));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Значок, который окно само сообщает системе (HICON, освобождать не нужно).</summary>
    public static IntPtr GetWindowIcon(IntPtr hwnd)
    {
        foreach (int kind in new[] { 1, 2, 0 }) // ICON_BIG, ICON_SMALL2, ICON_SMALL
        {
            if (SendMessageTimeout(hwnd, 0x7F, new IntPtr(kind), IntPtr.Zero, 2, 100, out var r) != IntPtr.Zero && r != IntPtr.Zero)
                return r;
        }
        var c = GetClassLongPtr(hwnd, -14); // GCL_HICON
        if (c != IntPtr.Zero) return c;
        return GetClassLongPtr(hwnd, -34);  // GCL_HICONSM
    }

    public static IntPtr Foreground() => GetForegroundWindow();

    /// <summary>Выводит наше окно на передний план, даже если Windows не разрешает это фоновым программам.</summary>
    public static void ForceForeground(IntPtr h)
    {
        keybd_event(0x12, 0, 0, UIntPtr.Zero); // нажатие Alt снимает блокировку смены фокуса
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        SetForegroundWindow(h);
    }

    public static void Activate(IntPtr h)
    {
        if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
        SetForegroundWindow(h);
    }

    public static void Minimize(IntPtr h) => ShowWindow(h, 6); // SW_MINIMIZE

    /// <summary>Просит окно закрыться так же, как кнопка «крестик» (приложение может спросить про сохранение).</summary>
    public static void PostClose(IntPtr h) => PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE

    /// <summary>Нажимает клавишу Win, чтобы открыть меню «Пуск».</summary>
    public static void PressWinKey()
    {
        keybd_event(0x5B, 0, 0, UIntPtr.Zero);
        keybd_event(0x5B, 0, 2, UIntPtr.Zero);
    }

    /// <summary>Нажимает сочетание Win + клавиша (Win+A — быстрые параметры, Win+Tab — обзор задач).</summary>
    public static void PressWinCombo(byte key)
    {
        keybd_event(0x5B, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 2, UIntPtr.Zero);
        keybd_event(0x5B, 0, 2, UIntPtr.Zero);
    }

    /// <summary>true, если на переднем плане окно на весь экран или развёрнутое — видео-обои можно ставить на паузу.</summary>
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

    /// <summary>true, если на переднем плане окно закрывает монитор целиком (игра, полноэкранное видео в браузере) — бар прячется.</summary>
    public static bool IsForegroundFullscreen()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero || IsIconic(h)) return false;
        var cls = ClassName(h);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == (uint)Environment.ProcessId) return false;
        if (!GetWindowRect(h, out var r)) return false;

        var mon = MonitorFromWindow(h, 2); // MONITOR_DEFAULTTONEAREST
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
            return r.left <= mi.rcMonitor.left && r.top <= mi.rcMonitor.top && r.right >= mi.rcMonitor.right && r.bottom >= mi.rcMonitor.bottom;

        return r.left <= 0 && r.top <= 0 && r.right >= GetSystemMetrics(SM_CXSCREEN) && r.bottom >= GetSystemMetrics(SM_CYSCREEN);
    }

    // ---------- завершение задач ----------

    public sealed record ProcInfo(string Name, string Path, int[] Pids, long MemMb);

    static bool IsExplorer(string? exe) =>
        exe != null && string.Equals(System.IO.Path.GetFileName(exe), "explorer.exe", StringComparison.OrdinalIgnoreCase);

    static void KillPid(int pid)
    {
        try
        {
            if (pid == Environment.ProcessId) return;
            using var p = Process.GetProcessById(pid);
            p.Kill(true);
        }
        catch (Exception ex)
        {
            Settings.Log("kill " + pid + ": " + ex.Message);
        }
    }

    /// <summary>Завершает приложение, которому принадлежат окна. Окна Проводника только закрываются: иначе пропала бы вся оболочка Windows.</summary>
    public static void EndTask(IEnumerable<IntPtr> windows, string? exe)
    {
        if (IsExplorer(exe))
        {
            foreach (var w in windows) PostClose(w);
            return;
        }
        var pids = new HashSet<uint>();
        foreach (var w in windows)
        {
            GetWindowThreadProcessId(w, out uint pid);
            if (pid != 0) pids.Add(pid);
        }
        foreach (var pid in pids) KillPid((int)pid);
    }

    public static void EndProcesses(IEnumerable<int> pids)
    {
        foreach (var pid in pids) KillPid(pid);
    }

    static string FriendlyName(string path, string fallback)
    {
        try
        {
            var d = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(d)) return d.Trim();
        }
        catch { /* берём имя процесса */ }
        return fallback;
    }

    /// <summary>Программы пользователя без окон на баре (фоновые, висящие в трее). Системные процессы Windows не показываются.</summary>
    public static List<ProcInfo> BackgroundApps(HashSet<string> shownExes)
    {
        var result = new Dictionary<string, (List<int> Pids, long Mem, string Name)>(StringComparer.OrdinalIgnoreCase);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        int session = Process.GetCurrentProcess().SessionId;

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == Environment.ProcessId || p.SessionId != session) continue;
                var path = p.MainModule?.FileName;
                if (string.IsNullOrEmpty(path)) continue;
                if (path.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) continue;
                if (shownExes.Contains(path.ToLowerInvariant())) continue;

                if (!result.TryGetValue(path, out var e)) e = (new List<int>(), 0, FriendlyName(path, p.ProcessName));
                e.Pids.Add(p.Id);
                e.Mem += p.WorkingSet64;
                result[path] = e;
            }
            catch
            {
                // нет доступа к процессу — пропускаем
            }
            finally
            {
                p.Dispose();
            }
        }

        return result
            .Select(kv => new ProcInfo(kv.Value.Name, kv.Key, kv.Value.Pids.ToArray(), kv.Value.Mem / 1024 / 1024))
            .OrderByDescending(x => x.MemMb)
            .Take(16)
            .ToList();
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
