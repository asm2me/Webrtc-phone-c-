using System.Collections.Generic;
using WebRtcPhoneDialer.Core.Models;

namespace WebRtcPhoneDialer.Core.Interfaces
{
    public interface IAudioDeviceProvider
    {
        List<AudioDevice> GetInputDevices();
        List<AudioDevice> GetOutputDevices();
    }
}
