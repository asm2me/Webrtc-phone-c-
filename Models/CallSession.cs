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

        public string DurationDisplay
        {
            get
            {
                if (!EndTime.HasValue) return "--:--";
                var d = EndTime.Value - StartTime;
                return d.TotalHours >= 1
                    ? $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}"
                    : $"{d.Minutes}:{d.Seconds:D2}";
            }
        }

        public string DateDisplay
        {
            get
            {
                if (StartTime.Date == DateTime.Today)
                    return StartTime.ToString("HH:mm");
                if (StartTime.Date == DateTime.Today.AddDays(-1))
                    return "Yesterday " + StartTime.ToString("HH:mm");
                return StartTime.ToString("MMM dd, HH:mm");
            }
        }

        public string StateDisplay
        {
            get
            {
                return State switch
                {
                    CallState.Ended => "Completed",
                    CallState.Failed => ErrorMessage ?? "Failed",
                    _ => State.ToString()
                };
            }
        }
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
