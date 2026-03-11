using System.Collections.Generic;

namespace WebRtcPhoneDialer.Core.Ipc
{
    public class IpcMessage
    {
        public string Kind { get; set; } = "";                          // "cmd" | "evt"
        public string Name { get; set; } = "";
        public Dictionary<string, string?> Data { get; set; } = new Dictionary<string, string?>();
    }
}
