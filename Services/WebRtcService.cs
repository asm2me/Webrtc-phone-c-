using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Windows;
using WebRtcPhoneDialer.Models;

// Alias to avoid ambiguity with SIPSorcery.Net.IceServer
using AppIceServer = WebRtcPhoneDialer.Models.IceServer;

namespace WebRtcPhoneDialer.Services
{
    public enum RegistrationState { Unregistered, Registering, Registered, Failed }

    public class SipLogEventArgs : EventArgs
    {
        public string Direction { get; }   // ">>" = sent, "<<" = received
        public string Message  { get; }
        public SipLogEventArgs(string direction, string message)
        {
            Direction = direction;
            Message   = message;
        }
    }

    public class RtpLogEventArgs : EventArgs
    {
        public string Message { get; }
        public RtpLogEventArgs(string message) { Message = message; }
    }

    public class WebRtcService : IDisposable
    {
        private CallSession?   _currentCall;
        private WebRtcConfiguration _config;
        private DateTime       _callStartTime;
        private bool           _disposed = false;

        // SIP domain override (Settings > SIP Domain field)
        private string? _sipDomain;

        // SIPSorcery core objects
        private SIPTransport?                _sipTransport;
        private WssClientSipChannel?         _wssChannel;
        private SIPRegistrationUserAgent?    _regAgent;
        private SIPUserAgent?                _userAgent;
        private RTCPeerConnection?           _peerConnection;
        private WindowsAudioEndPoint?        _audioEndPoint;

        // Circular buffers for Debug window replay
        private readonly Queue<SipLogEventArgs> _sipLogBuffer = new();
        private const int SipLogBufferMax = 200;
        private readonly Queue<RtpLogEventArgs> _rtpLogBuffer = new();
        private const int RtpLogBufferMax = 200;

        public RegistrationState RegistrationState   { get; private set; } = RegistrationState.Unregistered;
        public string            RegistrationMessage { get; private set; } = "Not registered";
        public string?           LastCallFailureReason { get; private set; }

        public event EventHandler<RegistrationState>? RegistrationStateChanged;
        public event EventHandler<CallState>?         CallStateChanged;
        public event EventHandler<SipLogEventArgs>?   SipMessageLogged;
        public event EventHandler<RtpLogEventArgs>?   RtpDebugLogged;

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public WebRtcService()
        {
            _config = new WebRtcConfiguration();
            Logger.Info("WebRTC Service initialized (SIPSorcery)");
        }

        // ── History replay ────────────────────────────────────────────────────────

        public IEnumerable<SipLogEventArgs> GetSipLogHistory()
        {
            lock (_sipLogBuffer) return _sipLogBuffer.ToArray();
        }

        public IEnumerable<RtpLogEventArgs> GetRtpLogHistory()
        {
            lock (_rtpLogBuffer) return _rtpLogBuffer.ToArray();
        }

        // ── Configuration ─────────────────────────────────────────────────────────

        public WebRtcConfiguration GetConfiguration() => _config;

        public void ConfigureServers(string stunServer, string[] iceServers)
        {
            _config.StunServer = stunServer;
            _config.IceServers.Clear();
            foreach (var url in iceServers.Where(s => !string.IsNullOrWhiteSpace(s)))
                _config.IceServers.Add(new AppIceServer { Url = url.Trim() });
        }

        public void Configure(AppSettings settings)
        {
            _config.Username          = settings.Username;
            _config.Password          = settings.Password;
            _config.StunServer        = settings.StunServer;
            _config.TurnServer        = settings.TurnServer;
            _config.TurnUsername      = settings.TurnUsername;
            _config.TurnPassword      = settings.TurnPassword;
            _config.SignalingServerUrl = settings.SignalingServerUrl;
            _config.AuthToken         = settings.AuthToken;
            _config.EnableAudio       = settings.EnableAudio;
            _config.EchoCancellation  = settings.EchoCancellation;
            _config.NoiseSuppression  = settings.NoiseSuppression;
            _config.InputVolume       = settings.InputVolume;
            _config.OutputVolume      = settings.OutputVolume;
            _config.AudioCodecName    = settings.AudioCodecName;
            _config.EnableVideo       = settings.EnableVideo;
            _config.VideoCodecName    = settings.VideoCodecName;

            _sipDomain = string.IsNullOrWhiteSpace(settings.SipDomain) ? null : settings.SipDomain.Trim();

            _config.IceServers.Clear();
            foreach (var line in settings.IceServers.Split('\n'))
            {
                var url = line.Trim();
                if (!string.IsNullOrEmpty(url))
                    _config.IceServers.Add(new AppIceServer { Url = url });
            }

            Logger.Info($"Configuration applied: Signaling={settings.SignalingServerUrl}");
        }

        // ── Registration ──────────────────────────────────────────────────────────

        public async Task RegisterAsync()
        {
            if (string.IsNullOrEmpty(_config.SignalingServerUrl))
            {
                RegistrationMessage = "Signaling server URL not configured. Open Settings.";
                SetRegistrationState(RegistrationState.Failed);
                return;
            }

            TearDown();

            SetRegistrationState(RegistrationState.Registering);
            RegistrationMessage = "Connecting...";

            try
            {
                var sigUri = new Uri(_config.SignalingServerUrl);
                var host   = _sipDomain ?? sigUri.Host;
                var user   = _config.Username ?? "";
                var pass   = _config.Password ?? "";

                // DNS resolve so we can build a SIPEndPoint (requires IPAddress)
                var addresses = await Dns.GetHostAddressesAsync(sigUri.Host);
                var serverIp  = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                                ?? addresses.First();
                var proxyEp   = new SIPEndPoint(SIPProtocolsEnum.wss, serverIp, sigUri.Port);

                // Determine local IP for Via/Contact headers
                var localIp = GetLocalIpForRemote(serverIp);
                var localSipEp = new SIPEndPoint(SIPProtocolsEnum.wss, localIp, 0);

                LogRtp($"Resolved {sigUri.Host} → {serverIp}  Local: {localIp}  Proxy: {proxyEp}");
                LogRtp($"Connecting WebSocket to {sigUri}");

                // Create our custom channel that uses the FULL URI (including /ws path)
                // and bypasses TLS certificate validation for self-signed certs
                _wssChannel = new WssClientSipChannel(sigUri, localSipEp, proxyEp);
                await _wssChannel.ConnectAsync();

                LogRtp($"WebSocket connected — State: {_wssChannel != null}");

                _sipTransport = new SIPTransport();
                _sipTransport.AddSIPChannel(_wssChannel);

                // Wire SIP message tracing → debug window
                _sipTransport.SIPRequestInTraceEvent  += (_, _, req)  => LogSipReceived(req.ToString());
                _sipTransport.SIPResponseInTraceEvent += (_, _, resp) => LogSipReceived(resp.ToString());
                _sipTransport.SIPRequestOutTraceEvent  += (_, _, req)  => LogSipSent(req.ToString());
                _sipTransport.SIPResponseOutTraceEvent += (_, _, resp) => LogSipSent(resp.ToString());

                // SIPUserAgent (for outbound calls)
                _userAgent = new SIPUserAgent(_sipTransport, proxyEp, false, null);
                _userAgent.OnCallHungup      += OnRemoteHangup;
                _userAgent.ClientCallAnswered += OnCallAnswered;
                _userAgent.ClientCallFailed  += OnCallFailed;
                _userAgent.ClientCallRinging += OnCallRinging;
                _userAgent.ClientCallTrying  += (_, _) => Logger.Debug("INVITE 100 Trying");

                // Registration agent (handles REGISTER + auth challenge + keep-alive)
                _regAgent = new SIPRegistrationUserAgent(_sipTransport, user, pass, $"sip:{user}@{host}", 3600);
                _regAgent.OutboundProxy = proxyEp;
                _regAgent.RegistrationSuccessful += OnRegistrationSuccessful;
                _regAgent.RegistrationFailed     += OnRegistrationFailed;

                _regAgent.Start();

                LogRtp($"Registration agent started — {user}@{host}");
                Logger.Info($"Registration started for {user}@{host} via {_config.SignalingServerUrl}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Registration setup failed");
                LogRtp($"Registration error: {ex.GetType().Name}: {ex.Message}");
                RegistrationMessage = $"Error: {ex.Message}";
                SetRegistrationState(RegistrationState.Failed);
            }
        }

        private void OnRegistrationSuccessful(SIPURI uri, SIPResponse resp)
        {
            RegistrationMessage = $"Registered as {uri.User}@{uri.HostAddress}";
            SetRegistrationState(RegistrationState.Registered);
            Logger.Info($"SIP registered: {uri}");
        }

        private void OnRegistrationFailed(SIPURI uri, SIPResponse? failResponse, string errorMessage)
        {
            var reason = !string.IsNullOrEmpty(errorMessage) ? errorMessage
                        : failResponse != null ? $"{failResponse.Status} {failResponse.ReasonPhrase}"
                        : "unknown";
            RegistrationMessage = $"Registration failed: {reason}";
            LogRtp($"[REG FAILED] {reason}");
            SetRegistrationState(RegistrationState.Failed);
            Logger.Warn($"SIP registration failed: {reason}");
        }

        // ── Call management ───────────────────────────────────────────────────────

        public async Task InitiateCallAsync(string remoteParty)
        {
            if (_userAgent == null)
                throw new InvalidOperationException("Not connected. Please register first.");

            if (_currentCall != null &&
                (_currentCall.State == CallState.Initiating ||
                 _currentCall.State == CallState.Ringing    ||
                 _currentCall.State == CallState.Connected))
                throw new InvalidOperationException("A call is already active.");

            LastCallFailureReason = null;
            var host = _sipDomain ?? new Uri(_config.SignalingServerUrl!).Host;

            _currentCall = new CallSession
            {
                RemoteParty = remoteParty,
                State       = CallState.Initiating,
                StartTime   = DateTime.Now
            };
            SetCallState(CallState.Initiating);

            _peerConnection = BuildPeerConnection();

            var callUri = SIPURI.ParseSIPURIRelaxed($"sip:{remoteParty}@{host}");
            Logger.Info($"Calling {callUri}");
            LogRtp("WebRTC peer connection created — ICE gathering started");

            // SIPSorcery handles: SDP offer, ICE, DTLS handshake, 401 re-auth, ACK
            bool result = await _userAgent.Call(callUri.ToString(), null, null, _peerConnection);
            if (!result)
                Logger.Warn("Call initiation returned false — waiting for callbacks");
        }

        public async Task EndCallAsync()
        {
            if (_currentCall == null) return;
            _currentCall.EndTime = DateTime.Now;
            try
            {
                if (_userAgent?.IsCallActive == true)
                    _userAgent.Hangup();
            }
            catch (Exception ex) { Logger.Warn(ex, "Error hanging up"); }

            CleanupMedia();
            SetCallState(CallState.Ended);
            _currentCall = null;
            await Task.CompletedTask;
        }

        // ── SIPUserAgent call-state callbacks ─────────────────────────────────────

        private void OnCallRinging(ISIPClientUserAgent uac, SIPResponse resp)
        {
            Logger.Info("Call ringing");
            SetCallState(CallState.Ringing);
        }

        private void OnCallAnswered(ISIPClientUserAgent uac, SIPResponse resp)
        {
            Logger.Info("Call answered — starting audio");
            StartAudio();
            SetCallState(CallState.Connected);
            LogRtp("Call connected — audio streams active (mic → RTP, RTP → speaker)");
        }

        private void OnCallFailed(ISIPClientUserAgent uac, string reason, SIPResponse? failResponse)
        {
            LastCallFailureReason = reason;
            Logger.Warn($"Call failed: {reason}");
            if (_currentCall != null) _currentCall.ErrorMessage = reason;
            CleanupMedia();
            SetCallState(CallState.Failed);
            _currentCall = null;
        }

        private void OnRemoteHangup(SIPDialogue dialogue)
        {
            Logger.Info("Remote party hung up");
            CleanupMedia();
            if (_currentCall != null)
            {
                _currentCall.EndTime = DateTime.Now;
                SetCallState(CallState.Ended);
                _currentCall = null;
            }
        }

        // ── WebRTC peer connection ─────────────────────────────────────────────────

        private RTCPeerConnection BuildPeerConnection()
        {
            var iceServers = new List<RTCIceServer>();

            if (!string.IsNullOrWhiteSpace(_config.StunServer))
                iceServers.Add(new RTCIceServer { urls = _config.StunServer });

            if (!string.IsNullOrWhiteSpace(_config.TurnServer))
                iceServers.Add(new RTCIceServer
                {
                    urls       = _config.TurnServer,
                    username   = _config.TurnUsername,
                    credential = _config.TurnPassword
                });

            foreach (var ice in _config.IceServers.Where(s => !string.IsNullOrWhiteSpace(s.Url)))
                if (iceServers.All(x => x.urls != ice.Url))
                    iceServers.Add(new RTCIceServer { urls = ice.Url! });

            if (iceServers.Count == 0)
                iceServers.Add(new RTCIceServer { urls = "stun:stun.l.google.com:19302" });

            var pc = new RTCPeerConnection(new RTCConfiguration { iceServers = iceServers });

            // Windows audio: microphone → encode → RTP; RTP → decode → speaker
            _audioEndPoint = new WindowsAudioEndPoint(new AudioEncoder(), -1, -1, false, false);
            _audioEndPoint.RestrictFormats(f => f.Codec == AudioCodecsEnum.PCMU ||
                                                f.Codec == AudioCodecsEnum.PCMA);

            var audioTrack = new MediaStreamTrack(
                _audioEndPoint.GetAudioSourceFormats(),
                MediaStreamStatusEnum.SendRecv);
            pc.addTrack(audioTrack);

            _audioEndPoint.OnAudioSourceEncodedSample += pc.SendAudio;

            pc.OnAudioFormatsNegotiated += formats =>
            {
                var fmt = formats.First();
                _audioEndPoint.SetAudioSourceFormat(fmt);
                _audioEndPoint.SetAudioSinkFormat(fmt);
                LogRtp($"Audio codec negotiated: {fmt.Codec} PT={fmt.FormatID}");
            };

            pc.OnRtpPacketReceived += (ep, media, pkt) =>
            {
                if (media == SDPMediaTypesEnum.audio)
                    _audioEndPoint?.GotAudioRtp(ep,
                        pkt.Header.SyncSource,
                        pkt.Header.SequenceNumber,
                        pkt.Header.Timestamp,
                        pkt.Header.PayloadType,
                        pkt.Header.MarkerBit == 1,
                        pkt.Payload);
            };

            // ICE / DTLS state logging
            pc.oniceconnectionstatechange += state =>
                LogRtp($"ICE connection: {state}");

            pc.onconnectionstatechange += state =>
                LogRtp($"WebRTC connection: {state}");

            pc.onicecandidate += candidate =>
            {
                if (candidate != null)
                    LogRtp($"ICE candidate: {candidate.candidate}");
            };

            return pc;
        }

        private void StartAudio()
        {
            try
            {
                _audioEndPoint?.StartAudio();
                LogRtp("Windows audio started (mic capture + speaker playback)");
            }
            catch (Exception ex)
            {
                LogRtp($"Audio start error: {ex.Message}");
                Logger.Warn(ex, "Failed to start audio");
            }
        }

        private void CleanupMedia()
        {
            if (_audioEndPoint != null)
            {
                try { _audioEndPoint.CloseAudio(); } catch { }
                _audioEndPoint = null;
                LogRtp("Audio endpoint closed");
            }

            if (_peerConnection != null)
            {
                try { _peerConnection.Close("call ended"); } catch { }
                _peerConnection = null;
            }
        }

        // ── State helpers ─────────────────────────────────────────────────────────

        public CallSession?  GetCurrentCall() => _currentCall;

        public TimeSpan GetCallDuration()
        {
            if (_currentCall == null || _currentCall.State != CallState.Connected)
                return TimeSpan.Zero;
            return DateTime.Now - _callStartTime;
        }

        /// <summary>
        /// Returns the local IP address that the OS would use to reach <paramref name="remoteIp"/>.
        /// Uses a connected UDP socket (no actual packet sent) to determine the best local interface.
        /// </summary>
        private static IPAddress GetLocalIpForRemote(IPAddress remoteIp)
        {
            try
            {
                using var s = new Socket(remoteIp.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                s.Connect(remoteIp, 80);
                return ((IPEndPoint)s.LocalEndPoint!).Address;
            }
            catch
            {
                return IPAddress.Loopback;
            }
        }

        private void SetRegistrationState(RegistrationState state)
        {
            RegistrationState = state;
            RegistrationStateChanged?.Invoke(this, state);
        }

        private void SetCallState(CallState state)
        {
            if (_currentCall != null)
                _currentCall.State = state;
            if (state == CallState.Connected)
                _callStartTime = DateTime.Now;
            CallStateChanged?.Invoke(this, state);
        }

        // ── Logging helpers ───────────────────────────────────────────────────────

        private void LogSipSent(string message)
        {
            var entry = new SipLogEventArgs(">>", message);
            lock (_sipLogBuffer)
            {
                _sipLogBuffer.Enqueue(entry);
                while (_sipLogBuffer.Count > SipLogBufferMax) _sipLogBuffer.Dequeue();
            }
            Logger.Debug($"SIP SEND: {FirstLine(message)}");
            SipMessageLogged?.Invoke(this, entry);
        }

        private void LogSipReceived(string message)
        {
            var entry = new SipLogEventArgs("<<", message);
            lock (_sipLogBuffer)
            {
                _sipLogBuffer.Enqueue(entry);
                while (_sipLogBuffer.Count > SipLogBufferMax) _sipLogBuffer.Dequeue();
            }
            Logger.Debug($"SIP RECV: {FirstLine(message)}");
            SipMessageLogged?.Invoke(this, entry);
        }

        private void LogRtp(string message)
        {
            var entry = new RtpLogEventArgs(message);
            lock (_rtpLogBuffer)
            {
                _rtpLogBuffer.Enqueue(entry);
                while (_rtpLogBuffer.Count > RtpLogBufferMax) _rtpLogBuffer.Dequeue();
            }
            Logger.Debug($"RTP: {message}");
            RtpDebugLogged?.Invoke(this, entry);
        }

        private static string FirstLine(string msg) =>
            msg.Split('\n')[0].Trim();

        // ── Teardown ──────────────────────────────────────────────────────────────

        private void TearDown()
        {
            try { _regAgent?.Stop(); } catch { }
            _regAgent = null;

            CleanupMedia();

            try { if (_userAgent?.IsCallActive == true) _userAgent.Hangup(); } catch { }
            _userAgent = null;

            if (_sipTransport != null)
            {
                try { _sipTransport.Shutdown(); } catch { }
                _sipTransport = null;
            }

            try { _wssChannel?.Close(); } catch { }
            _wssChannel = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                TearDown();
                _currentCall = null;
                Logger.Info("WebRTC Service disposed");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error disposing WebRTC Service");
            }
        }

        ~WebRtcService() => Dispose();
    }
}
