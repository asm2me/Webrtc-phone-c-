using System;

namespace WebRtcPhoneDialer.Core.Events
{
    public class RtpLogEventArgs : EventArgs
    {
        public string Message { get; }
        public RtpLogEventArgs(string message) { Message = message; }
    }
}
