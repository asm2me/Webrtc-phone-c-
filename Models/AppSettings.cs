using System;
using System.IO;
using Newtonsoft.Json;

namespace WebRtcPhoneDialer.Models
{
    public class AppSettings
    {
        // WebRTC / Signaling
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string StunServer { get; set; } = "stun:stun.l.google.com:19302";
        public string TurnServer { get; set; } = string.Empty;
        public string TurnUsername { get; set; } = string.Empty;
        public string TurnPassword { get; set; } = string.Empty;
        public string IceServers { get; set; } = "stun:stun.l.google.com:19302";
        public string SignalingServerUrl { get; set; } = string.Empty;
        public string SipDomain { get; set; } = string.Empty;
        public string AuthToken { get; set; } = string.Empty;

        // Audio
        public bool EnableAudio { get; set; } = true;
        public string InputDeviceId { get; set; } = string.Empty;
        public string OutputDeviceId { get; set; } = string.Empty;
        public int InputVolume { get; set; } = 80;
        public int OutputVolume { get; set; } = 80;
        public bool EchoCancellation { get; set; } = true;
        public bool NoiseSuppression { get; set; } = true;

        // Ringtone
        public string RingDeviceId { get; set; } = string.Empty;
        public int RingVolume { get; set; } = 80;
        public string RingtoneName { get; set; } = "Default";

        // Codecs
        public string AudioCodecName { get; set; } = "Opus";
        public bool EnableVideo { get; set; } = false;
        public string VideoCodecName { get; set; } = "VP8";

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WebRtcPhoneDialer",
            "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch { }
        }
    }
}
