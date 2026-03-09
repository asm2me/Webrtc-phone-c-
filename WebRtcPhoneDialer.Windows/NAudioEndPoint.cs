using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NAudio.Wave;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;

namespace WebRtcPhoneDialer.Windows
{
    /// <summary>
    /// Custom NAudio-based audio endpoint implementing IAudioSource and IAudioSink
    /// for .NET Framework 4.8.1 compatibility (replaces SIPSorceryMedia.Windows).
    /// </summary>
    public class NAudioEndPoint : IAudioSource, IAudioSink, IDisposable
    {
        private const int SAMPLE_RATE = 8000;
        private const int CHANNELS = 1;
        private const int BITS_PER_SAMPLE = 16;

        private readonly IAudioEncoder _encoder;
        private readonly int _inputDeviceIndex;
        private readonly int _outputDeviceIndex;

        private WaveInEvent? _waveIn;
        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _waveProvider;

        private AudioFormat _sendFormat;
        private AudioFormat _recvFormat;
        private List<AudioFormat> _supportedFormats;
        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;

        // IAudioSource events
        public event EncodedSampleDelegate? OnAudioSourceEncodedSample;
        public event Action<EncodedAudioFrame>? OnAudioSourceEncodedFrameReady;
        public event RawAudioSampleDelegate? OnAudioSourceRawSample;
        public event SourceErrorDelegate? OnAudioSourceError;

        // IAudioSink events
        public event SourceErrorDelegate? OnAudioSinkError;

        public NAudioEndPoint(IAudioEncoder encoder, int inputDeviceIndex = -1, int outputDeviceIndex = -1)
        {
            _encoder = encoder;
            _inputDeviceIndex = inputDeviceIndex;
            _outputDeviceIndex = outputDeviceIndex;
            _supportedFormats = new List<AudioFormat>(_encoder.SupportedFormats);

            // Default to first supported format
            if (_supportedFormats.Count > 0)
            {
                _sendFormat = _supportedFormats[0];
                _recvFormat = _supportedFormats[0];
            }
        }

        // ── IAudioSource ────────────────────────────────────────────────────────

        public List<AudioFormat> GetAudioSourceFormats() => _supportedFormats;

        public void SetAudioSourceFormat(AudioFormat audioFormat)
        {
            _sendFormat = audioFormat;
        }

        public void RestrictFormats(Func<AudioFormat, bool> filter)
        {
            _supportedFormats = _supportedFormats.Where(filter).ToList();
        }

        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate,
            uint durationMilliseconds, short[] sample)
        {
            if (_isPaused || _isClosed) return;

            var encoded = _encoder.EncodeAudio(sample, _sendFormat);
            if (encoded != null && encoded.Length > 0)
            {
                uint durationRtp = (uint)(samplingRate == AudioSamplingRatesEnum.Rate8KHz
                    ? durationMilliseconds * 8
                    : durationMilliseconds * ((int)samplingRate / 1000));
                OnAudioSourceEncodedSample?.Invoke(durationRtp, encoded);
            }
        }

        public bool HasEncodedAudioSubscribers() => OnAudioSourceEncodedSample != null;

        public bool IsAudioSourcePaused() => _isPaused;

        public Task StartAudio()
        {
            if (_isStarted || _isClosed) return Task.CompletedTask;

            try
            {
                if (_inputDeviceIndex >= 0 || _inputDeviceIndex == -1)
                {
                    int devIdx = _inputDeviceIndex == -1 ? 0 : _inputDeviceIndex;

                    if (devIdx < WaveIn.DeviceCount)
                    {
                        _waveIn = new WaveInEvent
                        {
                            DeviceNumber = devIdx,
                            WaveFormat = new WaveFormat(SAMPLE_RATE, BITS_PER_SAMPLE, CHANNELS),
                            BufferMilliseconds = 20
                        };
                        _waveIn.DataAvailable += WaveIn_DataAvailable;
                        _waveIn.StartRecording();
                    }
                }
            }
            catch (Exception ex)
            {
                OnAudioSourceError?.Invoke($"Audio source start error: {ex.Message}");
            }

            _isStarted = true;
            _isPaused = false;
            return Task.CompletedTask;
        }

        public Task PauseAudio()
        {
            _isPaused = true;
            return Task.CompletedTask;
        }

        public Task ResumeAudio()
        {
            _isPaused = false;
            return Task.CompletedTask;
        }

        public Task CloseAudio()
        {
            _isClosed = true;
            CleanupInput();
            return Task.CompletedTask;
        }

        private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_isPaused || _isClosed) return;

            // Convert byte[] PCM16 to short[]
            int sampleCount = e.BytesRecorded / 2;
            short[] pcm = new short[sampleCount];
            Buffer.BlockCopy(e.Buffer, 0, pcm, 0, e.BytesRecorded);

            uint durationMs = (uint)(sampleCount * 1000 / SAMPLE_RATE);

            OnAudioSourceRawSample?.Invoke(AudioSamplingRatesEnum.Rate8KHz, durationMs, pcm);

            if (HasEncodedAudioSubscribers())
            {
                var encoded = _encoder.EncodeAudio(pcm, _sendFormat);
                if (encoded != null && encoded.Length > 0)
                {
                    uint durationRtp = durationMs * 8; // 8kHz = 8 samples per ms
                    OnAudioSourceEncodedSample?.Invoke(durationRtp, encoded);
                }
            }
        }

        // ── IAudioSink ──────────────────────────────────────────────────────────

        public List<AudioFormat> GetAudioSinkFormats() => _supportedFormats;

        public void SetAudioSinkFormat(AudioFormat audioFormat)
        {
            _recvFormat = audioFormat;
        }

        public void GotAudioRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum,
            uint timestamp, int payloadID, bool marker, byte[] payload)
        {
            GotEncodedMediaFrame(new EncodedAudioFrame(0, _recvFormat, 20, payload));
        }

        public void GotEncodedMediaFrame(EncodedAudioFrame encodedMediaFrame)
        {
            if (_isPaused || _isClosed) return;

            try
            {
                var decoded = _encoder.DecodeAudio(encodedMediaFrame.EncodedAudio,
                    encodedMediaFrame.AudioFormat);

                if (decoded != null && decoded.Length > 0 && _waveProvider != null)
                {
                    byte[] pcmBytes = new byte[decoded.Length * 2];
                    Buffer.BlockCopy(decoded, 0, pcmBytes, 0, pcmBytes.Length);
                    _waveProvider.AddSamples(pcmBytes, 0, pcmBytes.Length);
                }
            }
            catch (Exception ex)
            {
                OnAudioSinkError?.Invoke($"Audio sink decode error: {ex.Message}");
            }
        }

        public Task StartAudioSink()
        {
            try
            {
                int devIdx = _outputDeviceIndex == -1 ? 0 : _outputDeviceIndex;

                if (devIdx < WaveOut.DeviceCount)
                {
                    _waveProvider = new BufferedWaveProvider(
                        new WaveFormat(SAMPLE_RATE, BITS_PER_SAMPLE, CHANNELS))
                    {
                        BufferDuration = TimeSpan.FromSeconds(1),
                        DiscardOnBufferOverflow = true
                    };

                    _waveOut = new WaveOutEvent { DeviceNumber = devIdx };
                    _waveOut.Init(_waveProvider);
                    _waveOut.Play();
                }
            }
            catch (Exception ex)
            {
                OnAudioSinkError?.Invoke($"Audio sink start error: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public Task PauseAudioSink()
        {
            _waveOut?.Pause();
            return Task.CompletedTask;
        }

        public Task ResumeAudioSink()
        {
            _waveOut?.Play();
            return Task.CompletedTask;
        }

        public Task CloseAudioSink()
        {
            CleanupOutput();
            return Task.CompletedTask;
        }

        // ── Cleanup ─────────────────────────────────────────────────────────────

        private void CleanupInput()
        {
            if (_waveIn != null)
            {
                try { _waveIn.StopRecording(); } catch { }
                _waveIn.DataAvailable -= WaveIn_DataAvailable;
                _waveIn.Dispose();
                _waveIn = null;
            }
        }

        private void CleanupOutput()
        {
            if (_waveOut != null)
            {
                try { _waveOut.Stop(); } catch { }
                _waveOut.Dispose();
                _waveOut = null;
            }
            _waveProvider = null;
        }

        public void Dispose()
        {
            _isClosed = true;
            CleanupInput();
            CleanupOutput();
        }
    }
}
