namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Runtime.InteropServices;
    using System.Runtime.Versioning;

    // The computer's master volume: read it, set it (0-100), mute / unmute.
    // macOS: osascript (tested). Windows: Core Audio (IAudioEndpointVolume), written from the documented interface,
    // NOT yet run on a Windows machine. Anything else: does nothing.
    public interface ISystemAudio
    {
        String Name { get; }

        Int32? GetVolume();

        Boolean SetVolume(Int32 percent);

        Boolean? GetMuted();

        Boolean SetMuted(Boolean muted);
    }

    public static class SystemAudio
    {
        public static ISystemAudio Create()
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    return new MacAudio();
                }

                if (OperatingSystem.IsWindows())
                {
                    return new WindowsAudio();
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "System audio control is not available");
            }

            return new NoAudio();
        }
    }

    public sealed class NoAudio : ISystemAudio
    {
        public String Name => "none";

        public Int32? GetVolume() => null;

        public Boolean SetVolume(Int32 percent) => false;

        public Boolean? GetMuted() => null;

        public Boolean SetMuted(Boolean muted) => false;
    }

    public sealed class MacAudio : ISystemAudio
    {
        public String Name => "macOS (osascript)";

        private static String Run(String script)
        {
            try
            {
                var psi = new ProcessStartInfo("osascript")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(script);
                using var p = Process.Start(psi);
                if (p == null)
                {
                    return null;
                }

                var output = p.StandardOutput.ReadToEnd().Trim();
                if (!p.WaitForExit(3000))
                {
                    p.Kill();
                    return null;
                }

                return p.ExitCode == 0 ? output : null;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "osascript failed");
                return null;
            }
        }

        // "missing value" (e.g. an HDMI display without a volume control) -> null
        public Int32? GetVolume() =>
            Int32.TryParse(Run("output volume of (get volume settings)"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

        public Boolean SetVolume(Int32 percent) =>
            Run($"set volume output volume {Math.Clamp(percent, 0, 100)}") != null;

        public Boolean? GetMuted() => Run("output muted of (get volume settings)") switch
        {
            "true" => true,
            "false" => false,
            _ => null,
        };

        public Boolean SetMuted(Boolean muted) =>
            Run($"set volume output muted {(muted ? "true" : "false")}") != null;
    }

    // Windows Core Audio. Untested on Windows (see the note at the top).
    [SupportedOSPlatform("windows")]
    public sealed class WindowsAudio : ISystemAudio
    {
        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorCom
        {
        }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] Int32 EnumAudioEndpoints(Int32 dataFlow, Int32 stateMask, out IntPtr devices);

            [PreserveSig] Int32 GetDefaultAudioEndpoint(Int32 dataFlow, Int32 role, out IMMDevice endpoint);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] Int32 Activate(ref Guid iid, Int32 clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out Object endpointVolume);
        }

        [ComImport]
        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] Int32 RegisterControlChangeNotify(IntPtr notify);

            [PreserveSig] Int32 UnregisterControlChangeNotify(IntPtr notify);

            [PreserveSig] Int32 GetChannelCount(out UInt32 count);

            [PreserveSig] Int32 SetMasterVolumeLevel(Single levelDb, ref Guid eventContext);

            [PreserveSig] Int32 SetMasterVolumeLevelScalar(Single level, ref Guid eventContext);

            [PreserveSig] Int32 GetMasterVolumeLevel(out Single levelDb);

            [PreserveSig] Int32 GetMasterVolumeLevelScalar(out Single level);

            [PreserveSig] Int32 SetChannelVolumeLevel(UInt32 channel, Single levelDb, ref Guid eventContext);

            [PreserveSig] Int32 SetChannelVolumeLevelScalar(UInt32 channel, Single level, ref Guid eventContext);

            [PreserveSig] Int32 GetChannelVolumeLevel(UInt32 channel, out Single levelDb);

            [PreserveSig] Int32 GetChannelVolumeLevelScalar(UInt32 channel, out Single level);

            [PreserveSig] Int32 SetMute([MarshalAs(UnmanagedType.Bool)] Boolean mute, ref Guid eventContext);

            [PreserveSig] Int32 GetMute([MarshalAs(UnmanagedType.Bool)] out Boolean mute);
        }

        private static readonly Guid EndpointVolumeIid = new("5CDF2C82-841E-4546-9722-0CF74078229A");
        private static Guid _noContext = Guid.Empty;

        public String Name => "Windows (Core Audio)";

        // eRender = 0, eMultimedia = 1; CLSCTX_ALL = 23
        private static IAudioEndpointVolume Endpoint()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0 || device == null)
            {
                return null;
            }

            var iid = EndpointVolumeIid;
            return device.Activate(ref iid, 23, IntPtr.Zero, out var obj) == 0 ? obj as IAudioEndpointVolume : null;
        }

        public Int32? GetVolume()
        {
            try
            {
                var ep = Endpoint();
                return ep != null && ep.GetMasterVolumeLevelScalar(out var v) == 0 ? (Int32)Math.Round(v * 100) : null;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Windows volume read failed");
                return null;
            }
        }

        public Boolean SetVolume(Int32 percent)
        {
            try
            {
                var ep = Endpoint();
                return ep != null && ep.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref _noContext) == 0;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Windows volume change failed");
                return false;
            }
        }

        public Boolean? GetMuted()
        {
            try
            {
                var ep = Endpoint();
                return ep != null && ep.GetMute(out var m) == 0 ? m : null;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Windows mute read failed");
                return null;
            }
        }

        public Boolean SetMuted(Boolean muted)
        {
            try
            {
                var ep = Endpoint();
                return ep != null && ep.SetMute(muted, ref _noContext) == 0;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Windows mute change failed");
                return false;
            }
        }
    }
}
