using System;
using System.Runtime.InteropServices;

namespace D4;

/// <summary>Громкость системы и выключение звука через Core Audio.</summary>
internal static class AudioVolume
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    /// <summary>Основное устройство вывода звука. Вызывающий обязан освободить объект через Marshal.ReleaseComObject.</summary>
    static IAudioEndpointVolume? Endpoint()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorObject();
            // 0 = eRender (вывод), 1 = eMultimedia
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, 23, IntPtr.Zero, out var obj) != 0) return null; // 23 = CLSCTX_ALL
            return (IAudioEndpointVolume)obj;
        }
        finally
        {
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    public static bool TryGet(out double volume, out bool muted)
    {
        volume = 0;
        muted = false;
        var ep = Endpoint();
        if (ep == null) return false;
        try
        {
            if (ep.GetMasterVolumeLevelScalar(out float v) != 0) return false;
            ep.GetMute(out bool m);
            volume = v;
            muted = m;
            return true;
        }
        finally
        {
            Marshal.ReleaseComObject(ep);
        }
    }

    public static void SetVolume(double volume)
    {
        var ep = Endpoint();
        if (ep == null) return;
        try
        {
            var ctx = Guid.Empty;
            ep.SetMasterVolumeLevelScalar((float)Math.Clamp(volume, 0, 1), ref ctx);
        }
        finally
        {
            Marshal.ReleaseComObject(ep);
        }
    }

    public static void SetMuted(bool muted)
    {
        var ep = Endpoint();
        if (ep == null) return;
        try
        {
            var ctx = Guid.Empty;
            ep.SetMute(muted, ref ctx);
        }
        finally
        {
            Marshal.ReleaseComObject(ep);
        }
    }
}
