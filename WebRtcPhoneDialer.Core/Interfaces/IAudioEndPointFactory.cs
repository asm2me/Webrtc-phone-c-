using SIPSorceryMedia.Abstractions;

namespace WebRtcPhoneDialer.Core.Interfaces
{
    public interface IAudioEndPointFactory
    {
        IAudioSource CreateAudioSource(int inputDeviceIndex = -1);
        IAudioSink CreateAudioSink(int outputDeviceIndex = -1);
    }
}
