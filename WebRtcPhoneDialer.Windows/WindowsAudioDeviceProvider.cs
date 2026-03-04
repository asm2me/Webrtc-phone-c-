using System.Collections.Generic;
using System.Runtime.InteropServices;
using WebRtcPhoneDialer.Core.Interfaces;
using WebRtcPhoneDialer.Core.Models;

namespace WebRtcPhoneDialer.Windows
{
    public class WindowsAudioDeviceProvider : IAudioDeviceProvider
    {
        [DllImport("winmm.dll")]
        private static extern int waveInGetNumDevs();

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int waveInGetDevCaps(int uDeviceID, ref WAVEINCAPS lpCaps, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutGetNumDevs();

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int waveOutGetDevCaps(int uDeviceID, ref WAVEOUTCAPS lpCaps, int uSize);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct WAVEINCAPS
        {
            public short wMid;
            public short wPid;
            public int vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public int dwFormats;
            public short wChannels;
            public short wReserved1;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct WAVEOUTCAPS
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public ushort wReserved1;
            public uint dwSupport;
        }

        public List<AudioDevice> GetInputDevices()
        {
            var devices = new List<AudioDevice>();
            int count = waveInGetNumDevs();
            for (int i = 0; i < count; i++)
            {
                var caps = new WAVEINCAPS();
                if (waveInGetDevCaps(i, ref caps, Marshal.SizeOf(caps)) == 0)
                    devices.Add(new AudioDevice { Index = i, Name = caps.szPname });
            }
            return devices;
        }

        public List<AudioDevice> GetOutputDevices()
        {
            var devices = new List<AudioDevice>();
            int count = waveOutGetNumDevs();
            for (int i = 0; i < count; i++)
            {
                var caps = new WAVEOUTCAPS();
                if (waveOutGetDevCaps(i, ref caps, Marshal.SizeOf(caps)) == 0)
                    devices.Add(new AudioDevice { Index = i, Name = caps.szPname });
            }
            return devices;
        }
    }
}
