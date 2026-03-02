using System;

namespace WebRtcPhoneDialer.Models
{
    public class CallSession
    {
        public string CallId { get; set; } = Guid.NewGuid().ToString();
        public string RemoteParty { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public CallState State { get; set; } = CallState.Idle;
        public string? ErrorMessage { get; set; }
    }

    public enum CallState
    {
        Idle,
        Initiating,
        Ringing,
        Connected,
        OnHold,
        Ended,
        Failed
    }
}
