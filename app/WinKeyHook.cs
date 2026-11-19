using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace D4;

/// <summary>
/// Перехватывает одиночное нажатие клавиши Win и вместо системного меню «Пуск» открывает меню D4.
/// Сочетания с Win (Win+E, Win+D и т.д.) работают как обычно.
/// </summary>
public sealed class WinKeyHook : IDisposable
{
    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public UIntPtr extra;
    }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);

    const int WH_KEYBOARD_LL = 13;
    const uint LLKHF_INJECTED = 0x10;

    readonly HookProc proc;
    readonly Action pressed;
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    IntPtr hook;
    bool winDown, other;

    public WinKeyHook(Action onPressed)
    {
        pressed = onPressed;
        proc = Callback; // держим ссылку, иначе сборщик мусора удалит делегат
        using var p = Process.GetCurrentProcess();
        using var m = p.MainModule;
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(m?.ModuleName), 0);
        if (hook == IntPtr.Zero) Settings.Log("keyhook: не удалось установить хук, код " + Marshal.GetLastWin32Error());
    }

    IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0)
            {
                var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if ((k.flags & LLKHF_INJECTED) == 0)
                {
                    int msg = wParam.ToInt32();
                    bool down = msg == 0x100 || msg == 0x104;
                    bool up = msg == 0x101 || msg == 0x105;
                    bool isWin = k.vkCode == 0x5B || k.vkCode == 0x5C;

                    if (isWin)
                    {
                        if (down)
                        {
                            if (!winDown)
                            {
                                winDown = true;
                                other = false;
                            }
                        }
                        else if (up)
                        {
                            bool lone = winDown && !other;
                            winDown = false;
                            if (lone && !Native.IsForegroundFullscreen())
                            {
                                // «чужая» клавиша между нажатием и отпусканием Win отменяет открытие системного «Пуск»
                                keybd_event(0xE8, 0, 0, UIntPtr.Zero);
                                keybd_event(0xE8, 0, 2, UIntPtr.Zero);
                                dispatcher.BeginInvoke(pressed);
                            }
                        }
                    }
                    else if (winDown && down)
                    {
                        other = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Settings.Log("keyhook: " + ex.Message);
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }
    }
}
