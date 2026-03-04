using System.Collections.Generic;

namespace WebRtcPhoneDialer.Core.Models
{
    public class IceServer
    {
        public string? Url { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
    }

    public class WebRtcConfiguration
    {
        public List<IceServer> IceServers { get; set; } = new List<IceServer>();
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? StunServer { get; set; }
        public string? TurnServer { get; set; }
        public string? TurnUsername { get; set; }
        public string? TurnPassword { get; set; }
        public string? SignalingServerUrl { get; set; }
        public string? AuthToken { get; set; }
        public int? AudioCodec { get; set; }
        public string? AudioCodecName { get; set; }
        public int? VideoCodec { get; set; }
        public string? VideoCodecName { get; set; }
        public bool EnableAudio { get; set; } = true;
        public bool EnableVideo { get; set; } = false;

        // Audio processing
        public bool EchoCancellation { get; set; } = true;
        public bool NoiseSuppression { get; set; } = true;
        public int InputVolume { get; set; } = 80;
        public int OutputVolume { get; set; } = 80;
    }
}
