using System;
using System.Collections.Generic;
using WebRtcPhoneDialer.Models;

namespace WebRtcPhoneDialer.Services
{
    public class CallHistoryService
    {
        private List<CallSession> _callHistory = new List<CallSession>();

        public void AddCall(CallSession call)
        {
            if (call != null)
            {
                _callHistory.Add(call);
            }
        }

        public IReadOnlyList<CallSession> GetCallHistory()
        {
            return _callHistory.AsReadOnly();
        }

        public void ClearHistory()
        {
            _callHistory.Clear();
        }

        public int GetTotalCallCount()
        {
            return _callHistory.Count;
        }

        public TimeSpan GetTotalCallDuration()
        {
            TimeSpan total = TimeSpan.Zero;
            foreach (var call in _callHistory)
            {
                if (call.EndTime.HasValue)
                {
                    total += call.EndTime.Value - call.StartTime;
                }
            }
            return total;
        }
    }
}
