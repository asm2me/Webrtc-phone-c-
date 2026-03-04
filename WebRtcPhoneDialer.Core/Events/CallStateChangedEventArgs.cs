using System;
using WebRtcPhoneDialer.Core.Enums;
using WebRtcPhoneDialer.Core.Models;

namespace WebRtcPhoneDialer.Core.Events
{
    public class CallStateChangedEventArgs : EventArgs
    {
        public CallState PreviousState { get; }
        public CallState NewState { get; }
        public CallSession Call { get; }
        public string? Reason { get; }

        public CallStateChangedEventArgs(CallState previousState, CallState newState, CallSession call, string? reason = null)
        {
            PreviousState = previousState;
            NewState = newState;
            Call = call;
            Reason = reason;
        }
    }
}
