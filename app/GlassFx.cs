using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace D4;

/// <summary>Размытие фона за окном (акрил Windows) в режиме стекла. Окна должны быть без AllowsTransparency.</summary>
internal static class GlassFx
{
    [StructLayout(LayoutKind.Sequential)]
    struct ACCENT_POLICY
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WINCOMPATTRDATA
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref WINCOMPATTRDATA data);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

    static void SetBlur(IntPtr h, bool on)
    {
        var policy = new ACCENT_POLICY
        {
            AccentState = on ? 3 : 0, // ACCENT_ENABLE_BLURBEHIND: мягче и заметно легче акрила
            AccentFlags = 0,
            GradientColor = 0u,
        };
        int size = Marshal.SizeOf(policy);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WINCOMPATTRDATA { Attribute = 19, Data = ptr, SizeOfData = size }; // WCA_ACCENT_POLICY
            SetWindowCompositionAttribute(h, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>Включает и выключает размытие вместе с режимом стекла. roundCorners — скруглить окно средствами Windows 11.</summary>
    public static void Attach(Window w, bool roundCorners = false)
    {
        void Do()
        {
            try
            {
                var h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) return;
                var src = HwndSource.FromHwnd(h);
                if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
                SetBlur(h, ThemeManager.Glass);
                if (roundCorners)
                {
                    int pref = 2; // DWMWCP_ROUND
                    DwmSetWindowAttribute(h, 33, ref pref, sizeof(int));
                }
            }
            catch (Exception ex)
            {
                Settings.Log("glass: " + ex.Message);
            }
        }

        if (new WindowInteropHelper(w).Handle != IntPtr.Zero) Do();
        else w.SourceInitialized += (_, _) => Do();

        Action changed = Do;
        ThemeManager.Changed += changed;
        w.Closed += (_, _) => ThemeManager.Changed -= changed;
    }
}
