using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Windows;
using WebRtcPhoneDialer.Core.Interfaces;

namespace WebRtcPhoneDialer.Windows
{
    public class WindowsAudioEndPointFactory : IAudioEndPointFactory
    {
        public IAudioSource CreateAudioSource(int inputDeviceIndex = -1)
        {
            return new WindowsAudioEndPoint(new AudioEncoder(), inputDeviceIndex, -1, false, false);
        }

        public IAudioSink CreateAudioSink(int outputDeviceIndex = -1)
        {
            return new WindowsAudioEndPoint(new AudioEncoder(), -1, outputDeviceIndex, false, false);
        }
    }
}
