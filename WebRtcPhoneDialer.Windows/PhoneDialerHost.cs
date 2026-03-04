using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebRtcPhoneDialer.Core.Enums;
using WebRtcPhoneDialer.Core.Events;
using WebRtcPhoneDialer.Core.Interfaces;
using WebRtcPhoneDialer.Core.Models;
using WebRtcPhoneDialer.Core.Services;

namespace WebRtcPhoneDialer.Windows
{
    /// <summary>
    /// Convenience host for Windows desktop apps. Creates the phone service
    /// with Windows audio, manages settings persistence, and exposes the
    /// full IPhoneService API. This is the recommended single entry point
    /// for parent applications on Windows.
    ///
    /// Usage:
    /// <code>
    /// var host = new PhoneDialerHost();
    /// host.CallStateDetailChanged += (s, e) => { /* Ringing, Connected, Ended, etc. */ };
    /// host.RegistrationStateChanged += (s, e) => { /* Registered, Failed, etc. */ };
    /// host.SipMessageLogged += (s, e) => { /* SIP debug traffic */ };
    /// host.RtpDebugLogged += (s, e) => { /* RTP debug info */ };
    /// host.MicLevelChanged += (s, e) => { /* 0.0–1.0 audio level */ };
    ///
    /// host.LoadAndApplySettings();  // or host.Configure(mySettings);
    /// await host.RegisterAsync();
    /// await host.InitiateCallAsync("100");
    /// host.HoldCall();
    /// host.UnholdCall();
    /// await host.EndCallAsync();
    /// host.Unregister();
    /// host.Dispose();
    /// </code>
    /// </summary>
    public class PhoneDialerHost : IPhoneService
    {
        private readonly WebRtcService _service;
        private AppSettings _settings;

        /// <summary>The underlying phone service instance.</summary>
        public IPhoneService Service => _service;

        /// <summary>The current settings (loaded or configured).</summary>
        public AppSettings Settings => _settings;

        /// <summary>Audio device provider for enumerating Windows audio devices.</summary>
        public IAudioDeviceProvider AudioDevices { get; }

        public PhoneDialerHost()
        {
            _service = new WebRtcService(new WindowsAudioEndPointFactory());
            _settings = new AppSettings();
            AudioDevices = new WindowsAudioDeviceProvider();
        }

        /// <summary>
        /// Load settings from the default JSON file (%AppData%\WebRtcPhoneDialer\settings.json)
        /// and apply them to the service.
        /// </summary>
        public AppSettings LoadAndApplySettings()
        {
            _settings = AppSettings.Load();
            _service.Configure(_settings);
            return _settings;
        }

        /// <summary>
        /// Save settings to the default JSON file and apply them to the service.
        /// </summary>
        public void SaveAndApplySettings(AppSettings settings)
        {
            _settings = settings;
            _settings.Save();
            _service.Configure(_settings);
        }

        // ── IPhoneService delegation ────────────────────────────────────────────

        public RegistrationState RegistrationState => _service.RegistrationState;
        public string RegistrationMessage => _service.RegistrationMessage;
        public bool HasActiveCall => _service.HasActiveCall;
        public bool IsRegistered => _service.IsRegistered;
        public string? LastCallFailureReason => _service.LastCallFailureReason;

        public void Configure(AppSettings settings) { _settings = settings; _service.Configure(settings); }
        public WebRtcConfiguration GetConfiguration() => _service.GetConfiguration();

        public Task RegisterAsync() => _service.RegisterAsync();
        public void Unregister() => _service.Unregister();

        public Task InitiateCallAsync(string remoteParty) => _service.InitiateCallAsync(remoteParty);
        public Task EndCallAsync() => _service.EndCallAsync();
        public void HoldCall() => _service.HoldCall();
        public void UnholdCall() => _service.UnholdCall();
        public Task AnswerCallAsync() => _service.AnswerCallAsync();
        public void RejectCall() => _service.RejectCall();
        public void SendDtmf(byte tone) => _service.SendDtmf(tone);
        public void MuteMicrophone() => _service.MuteMicrophone();
        public void UnmuteMicrophone() => _service.UnmuteMicrophone();

        public CallSession? GetCurrentCall() => _service.GetCurrentCall();
        public TimeSpan GetCallDuration() => _service.GetCallDuration();
        public (long sent, long recv, long bytesSent, long bytesRecv) GetRtpStats() => _service.GetRtpStats();

        public IEnumerable<SipLogEventArgs> GetSipLogHistory() => _service.GetSipLogHistory();
        public IEnumerable<RtpLogEventArgs> GetRtpLogHistory() => _service.GetRtpLogHistory();

        // ── Events (forwarded from underlying service) ──────────────────────────

        public event EventHandler<RegistrationState>? RegistrationStateChanged
        {
            add => _service.RegistrationStateChanged += value;
            remove => _service.RegistrationStateChanged -= value;
        }

        public event EventHandler<CallState>? CallStateChanged
        {
            add => _service.CallStateChanged += value;
            remove => _service.CallStateChanged -= value;
        }

        public event EventHandler<CallStateChangedEventArgs>? CallStateDetailChanged
        {
            add => _service.CallStateDetailChanged += value;
            remove => _service.CallStateDetailChanged -= value;
        }

        public event EventHandler<SipLogEventArgs>? SipMessageLogged
        {
            add => _service.SipMessageLogged += value;
            remove => _service.SipMessageLogged -= value;
        }

        public event EventHandler<RtpLogEventArgs>? RtpDebugLogged
        {
            add => _service.RtpDebugLogged += value;
            remove => _service.RtpDebugLogged -= value;
        }

        public event EventHandler<float>? MicLevelChanged
        {
            add => _service.MicLevelChanged += value;
            remove => _service.MicLevelChanged -= value;
        }

        public event EventHandler<float>? SpeakerLevelChanged
        {
            add => _service.SpeakerLevelChanged += value;
            remove => _service.SpeakerLevelChanged -= value;
        }

        public event EventHandler<CallSession>? IncomingCall
        {
            add => _service.IncomingCall += value;
            remove => _service.IncomingCall -= value;
        }

        public void Dispose()
        {
            _service.Dispose();
        }
    }
}
