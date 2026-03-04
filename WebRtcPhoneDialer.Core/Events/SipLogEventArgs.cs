using System;

namespace WebRtcPhoneDialer.Core.Events
{
    public class SipLogEventArgs : EventArgs
    {
        public string Direction { get; }   // ">>" = sent, "<<" = received
        public string Message  { get; }
        public SipLogEventArgs(string direction, string message)
        {
            Direction = direction;
            Message   = message;
        }
    }
}
