using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebRtcPhoneDialer.Core.Enums;
using WebRtcPhoneDialer.Core.Events;
using WebRtcPhoneDialer.Core.Models;

namespace WebRtcPhoneDialer.Core.Interfaces
{
    /// <summary>
    /// Primary interface for parent applications to control the phone dialer.
    /// Provides methods for registration, call control, and configuration,
    /// plus events for monitoring call lifecycle and debug information.
    /// </summary>
    public interface IPhoneService : IDisposable
    {
        // ── State properties ────────────────────────────────────────────────────

        /// <summary>Current SIP registration state.</summary>
        RegistrationState RegistrationState { get; }

        /// <summary>Human-readable registration status message.</summary>
        string RegistrationMessage { get; }

        /// <summary>True when a call is active (initiating, ringing, connected, or on hold).</summary>
        bool HasActiveCall { get; }

        /// <summary>True when registered with the SIP server.</summary>
        bool IsRegistered { get; }

        /// <summary>Reason for the last call failure, if any.</summary>
        string? LastCallFailureReason { get; }

        // ── Configuration ───────────────────────────────────────────────────────

        /// <summary>Apply settings (SIP credentials, audio config, codecs, etc.).</summary>
        void Configure(AppSettings settings);

        /// <summary>Get the current active configuration.</summary>
        WebRtcConfiguration GetConfiguration();

        // ── Registration ────────────────────────────────────────────────────────

        /// <summary>Register with the SIP server using the configured credentials.</summary>
        Task RegisterAsync();

        /// <summary>Unregister from the SIP server.</summary>
        void Unregister();

        // ── Call control ────────────────────────────────────────────────────────

        /// <summary>Initiate an outgoing call to the specified number or SIP URI.</summary>
        Task InitiateCallAsync(string remoteParty);

        /// <summary>End the current active call (hangup).</summary>
        Task EndCallAsync();

        /// <summary>Place the current connected call on hold.</summary>
        void HoldCall();

        /// <summary>Resume a call that is currently on hold.</summary>
        void UnholdCall();

        /// <summary>Answer an incoming call.</summary>
        Task AnswerCallAsync();

        /// <summary>Reject an incoming call.</summary>
        void RejectCall();

        /// <summary>Send a DTMF tone during an active call (0-9, *, #).</summary>
        void SendDtmf(byte tone);

        /// <summary>Mute the microphone (stops sending audio, does not change call state).</summary>
        void MuteMicrophone();

        /// <summary>Unmute the microphone.</summary>
        void UnmuteMicrophone();

        // ── Call info ───────────────────────────────────────────────────────────

        /// <summary>Get the current call session, or null if no call is active.</summary>
        CallSession? GetCurrentCall();

        /// <summary>Get the elapsed duration of the current connected call.</summary>
        TimeSpan GetCallDuration();

        /// <summary>Get RTP packet statistics for the current call.</summary>
        (long sent, long recv, long bytesSent, long bytesRecv) GetRtpStats();

        // ── Debug / log access ──────────────────────────────────────────────────

        /// <summary>Get buffered SIP message log history.</summary>
        IEnumerable<SipLogEventArgs> GetSipLogHistory();

        /// <summary>Get buffered RTP debug log history.</summary>
        IEnumerable<RtpLogEventArgs> GetRtpLogHistory();

        // ── Events ──────────────────────────────────────────────────────────────

        /// <summary>Fired when SIP registration state changes.</summary>
        event EventHandler<RegistrationState>? RegistrationStateChanged;

        /// <summary>Fired when call state changes (simple enum).</summary>
        event EventHandler<CallState>? CallStateChanged;

        /// <summary>Fired when call state changes with full context (previous state, call session, reason).</summary>
        event EventHandler<CallStateChangedEventArgs>? CallStateDetailChanged;

        /// <summary>Fired for each SIP message sent or received (debug).</summary>
        event EventHandler<SipLogEventArgs>? SipMessageLogged;

        /// <summary>Fired for RTP debug log entries.</summary>
        event EventHandler<RtpLogEventArgs>? RtpDebugLogged;

        /// <summary>Fired with microphone audio level (0.0–1.0), ~20 fps.</summary>
        event EventHandler<float>? MicLevelChanged;

        /// <summary>Fired with speaker audio level (0.0–1.0), ~20 fps.</summary>
        event EventHandler<float>? SpeakerLevelChanged;

        /// <summary>Fired when an incoming call is received. The CallSession contains the caller info.</summary>
        event EventHandler<CallSession>? IncomingCall;

        /// <summary>Fired when the remote caller cancels (hangs up) while ringing.</summary>
        event EventHandler? IncomingCallCanceled;

        /// <summary>
        /// Fired every 5 seconds during a connected call with live network quality metrics
        /// (packet loss, jitter, bitrate, quality tier).
        /// </summary>
        event EventHandler<NetworkQualityMetrics>? NetworkQualityChanged;
    }
}
