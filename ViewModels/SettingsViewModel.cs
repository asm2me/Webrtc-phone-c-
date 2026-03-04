using System;
using System.Collections.Generic;
using System.IO;
using WebRtcPhoneDialer.Core.Models;

namespace WebRtcPhoneDialer.ViewModels
{
    public class SettingsViewModel : BaseViewModel
    {
        // WebRTC / Signaling
        private string _username;
        public string Username { get => _username; set { _username = value; OnPropertyChanged(); } }

        private string _password;
        public string Password { get => _password; set { _password = value; OnPropertyChanged(); } }

        private string _stunServer;
        public string StunServer { get => _stunServer; set { _stunServer = value; OnPropertyChanged(); } }

        private string _turnServer;
        public string TurnServer { get => _turnServer; set { _turnServer = value; OnPropertyChanged(); } }

        private string _turnUsername;
        public string TurnUsername { get => _turnUsername; set { _turnUsername = value; OnPropertyChanged(); } }

        private string _turnPassword;
        public string TurnPassword { get => _turnPassword; set { _turnPassword = value; OnPropertyChanged(); } }

        private string _iceServers;
        public string IceServers { get => _iceServers; set { _iceServers = value; OnPropertyChanged(); } }

        private string _signalingServerUrl;
        public string SignalingServerUrl { get => _signalingServerUrl; set { _signalingServerUrl = value; OnPropertyChanged(); } }

        private string _sipDomain;
        public string SipDomain { get => _sipDomain; set { _sipDomain = value; OnPropertyChanged(); } }

        private string _authToken;
        public string AuthToken { get => _authToken; set { _authToken = value; OnPropertyChanged(); } }

        // Audio
        private bool _enableAudio;
        public bool EnableAudio { get => _enableAudio; set { _enableAudio = value; OnPropertyChanged(); } }

        private string _inputDeviceId;
        public string InputDeviceId { get => _inputDeviceId; set { _inputDeviceId = value; OnPropertyChanged(); } }

        private string _outputDeviceId;
        public string OutputDeviceId { get => _outputDeviceId; set { _outputDeviceId = value; OnPropertyChanged(); } }

        private int _inputVolume;
        public int InputVolume { get => _inputVolume; set { _inputVolume = value; OnPropertyChanged(); } }

        private int _outputVolume;
        public int OutputVolume { get => _outputVolume; set { _outputVolume = value; OnPropertyChanged(); } }

        private bool _echoCancellation;
        public bool EchoCancellation { get => _echoCancellation; set { _echoCancellation = value; OnPropertyChanged(); } }

        private bool _noiseSuppression;
        public bool NoiseSuppression { get => _noiseSuppression; set { _noiseSuppression = value; OnPropertyChanged(); } }

        // Ringtone
        private string _ringDeviceId;
        public string RingDeviceId { get => _ringDeviceId; set { _ringDeviceId = value; OnPropertyChanged(); } }

        private int _ringVolume;
        public int RingVolume { get => _ringVolume; set { _ringVolume = value; OnPropertyChanged(); } }

        private string _ringtoneName;
        public string RingtoneName { get => _ringtoneName; set { _ringtoneName = value; OnPropertyChanged(); } }

        // Codecs
        private string _audioCodecName;
        public string AudioCodecName { get => _audioCodecName; set { _audioCodecName = value; OnPropertyChanged(); } }

        private bool _enableVideo;
        public bool EnableVideo { get => _enableVideo; set { _enableVideo = value; OnPropertyChanged(); } }

        private string _videoCodecName;
        public string VideoCodecName { get => _videoCodecName; set { _videoCodecName = value; OnPropertyChanged(); } }

        // Available options
        public List<string> AudioCodecOptions { get; } = new List<string> { "Opus", "PCMU (G.711 µ-law)", "PCMA (G.711 a-law)", "G.722" };
        public List<string> VideoCodecOptions { get; } = new List<string> { "VP8", "VP9", "H.264" };
        public List<string> RingtoneOptions { get; } = BuildRingtoneOptions();

        private static List<string> BuildRingtoneOptions()
        {
            var list = new List<string> { "Default" };
            try
            {
                var mediaDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
                if (Directory.Exists(mediaDir))
                {
                    foreach (var f in Directory.GetFiles(mediaDir, "*.wav"))
                        list.Add(Path.GetFileName(f));
                }
            }
            catch { }
            return list;
        }

        public SettingsViewModel(AppSettings settings)
        {
            _username = settings.Username;
            _password = settings.Password;
            _stunServer = settings.StunServer;
            _turnServer = settings.TurnServer;
            _turnUsername = settings.TurnUsername;
            _turnPassword = settings.TurnPassword;
            _iceServers = settings.IceServers;
            _signalingServerUrl = settings.SignalingServerUrl;
            _sipDomain = settings.SipDomain;
            _authToken = settings.AuthToken;

            _enableAudio = settings.EnableAudio;
            _inputDeviceId = settings.InputDeviceId;
            _outputDeviceId = settings.OutputDeviceId;
            _inputVolume = settings.InputVolume;
            _outputVolume = settings.OutputVolume;
            _echoCancellation = settings.EchoCancellation;
            _noiseSuppression = settings.NoiseSuppression;

            _ringDeviceId = settings.RingDeviceId;
            _ringVolume = settings.RingVolume;
            _ringtoneName = settings.RingtoneName;

            _audioCodecName = settings.AudioCodecName;
            _enableVideo = settings.EnableVideo;
            _videoCodecName = settings.VideoCodecName;
        }

        public AppSettings ApplyToSettings()
        {
            return new AppSettings
            {
                Username = Username,
                Password = Password,
                StunServer = StunServer,
                TurnServer = TurnServer,
                TurnUsername = TurnUsername,
                TurnPassword = TurnPassword,
                IceServers = IceServers,
                SignalingServerUrl = SignalingServerUrl,
                SipDomain = SipDomain,
                AuthToken = AuthToken,

                EnableAudio = EnableAudio,
                InputDeviceId = InputDeviceId,
                OutputDeviceId = OutputDeviceId,
                InputVolume = InputVolume,
                OutputVolume = OutputVolume,
                EchoCancellation = EchoCancellation,
                NoiseSuppression = NoiseSuppression,

                RingDeviceId = RingDeviceId,
                RingVolume = RingVolume,
                RingtoneName = RingtoneName,

                AudioCodecName = AudioCodecName,
                EnableVideo = EnableVideo,
                VideoCodecName = VideoCodecName
            };
        }
    }
}
