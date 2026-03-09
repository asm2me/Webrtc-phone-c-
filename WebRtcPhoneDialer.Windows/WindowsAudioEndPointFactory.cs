using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using WebRtcPhoneDialer.Core.Interfaces;

namespace WebRtcPhoneDialer.Windows
{
    public class WindowsAudioEndPointFactory : IAudioEndPointFactory
    {
        public IAudioSource CreateAudioSource(int inputDeviceIndex = -1)
        {
            return new NAudioEndPoint(new AudioEncoder(), inputDeviceIndex, -1);
        }

        public IAudioSink CreateAudioSink(int outputDeviceIndex = -1)
        {
            return new NAudioEndPoint(new AudioEncoder(), -1, outputDeviceIndex);
        }
    }
}
