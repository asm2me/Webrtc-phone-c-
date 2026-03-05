using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using WebRtcPhoneDialer.Core.Enums;
using WebRtcPhoneDialer.Core.Events;
using WebRtcPhoneDialer.Core.Interfaces;
using WebRtcPhoneDialer.Core.Models;

namespace WebRtcPhoneDialer.Core.Services
{
    public class WebRtcService : IPhoneService
    {
        private readonly IAudioEndPointFactory _audioFactory;
        private CallSession?   _currentCall;
        private WebRtcConfiguration _config;
        private DateTime       _callStartTime;
        private bool           _disposed = false;
        private bool           _isReconnecting = false;
        private Timer?         _sipKeepAliveTimer;
        private volatile bool  _micMuted = false;

        // SIP domain override (Settings > SIP Domain field)
        private string? _sipDomain;

        // NAT traversal — public IP discovered via SIP Via received or STUN
        private IPAddress? _publicIp;

        // RTP packet statistics
        private long _rtpPacketsSent;
        private long _rtpPacketsReceived;
        private long _rtpBytesSent;
        private long _rtpBytesReceived;
        private DateTime? _firstRtpSent;
        private DateTime? _firstRtpReceived;
        private Timer? _rtpStatsTimer;

        // Audio level metering (throttled)
        private DateTime _lastMicLevelTime;
        private DateTime _lastSpkLevelTime;
        private const int LevelUpdateMs = 50; // ~20 fps

        // SIPSorcery core objects
        private SIPTransport?                _sipTransport;
        private WssClientSipChannel?         _wssChannel;
        private SIPRegistrationUserAgent?    _regAgent;
        private SIPUserAgent?                _userAgent;
        private SIPEndPoint?                 _proxyEp;
        private IPAddress?                   _localIp;
        private RTPSession?                  _mediaSession;


        // Platform-abstracted audio endpoint
        private IAudioSource?                _audioSource;
        private IAudioSink?                  _audioSink;

        // Circular buffers for Debug window replay
        private readonly Queue<SipLogEventArgs> _sipLogBuffer = new();
        private const int SipLogBufferMax = 200;
        private readonly Queue<RtpLogEventArgs> _rtpLogBuffer = new();
        private const int RtpLogBufferMax = 200;

        public RegistrationState RegistrationState   { get; private set; } = RegistrationState.Unregistered;
        public string            RegistrationMessage { get; private set; } = "Not registered";
        public string?           LastCallFailureReason { get; private set; }

        // Existing events
        public event EventHandler<RegistrationState>? RegistrationStateChanged;
        public event EventHandler<CallState>?         CallStateChanged;
        public event EventHandler<SipLogEventArgs>?   SipMessageLogged;
        public event EventHandler<RtpLogEventArgs>?   RtpDebugLogged;
        public event EventHandler<float>?              MicLevelChanged;
        public event EventHandler<float>?              SpeakerLevelChanged;

        // Rich event with full call context
        public event EventHandler<CallStateChangedEventArgs>? CallStateDetailChanged;

        // Incoming call event
        public event EventHandler<CallSession>? IncomingCall;
        public event EventHandler? IncomingCallCanceled;

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public WebRtcService(IAudioEndPointFactory audioFactory)
        {
            _audioFactory = audioFactory ?? throw new ArgumentNullException(nameof(audioFactory));
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

                var addresses = await Dns.GetHostAddressesAsync(sigUri.Host);
                var serverIp  = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                                ?? addresses.First();
                var proxyEp   = new SIPEndPoint(SIPProtocolsEnum.wss, serverIp, sigUri.Port);
                _proxyEp = proxyEp;

                var localIp = GetLocalIpForRemote(serverIp);
                _localIp = localIp;
                var localSipEp = new SIPEndPoint(SIPProtocolsEnum.wss, localIp, 0);

                LogRtp($"Resolved {sigUri.Host} → {serverIp}  Local: {localIp}  Proxy: {proxyEp}");
                LogRtp($"Connecting WebSocket to {sigUri}");

                _wssChannel = new WssClientSipChannel(sigUri, localSipEp, proxyEp);
                _wssChannel.Disconnected += OnWssDisconnected;
                await _wssChannel.ConnectAsync();

                LogRtp($"WebSocket connected — State: {_wssChannel != null}");

                _sipTransport = new SIPTransport();
                // IMPORTANT: Only use WSS channel — prevent SIPSorcery from auto-creating UDP/TCP channels
                _sipTransport.AddSIPChannel(_wssChannel);
                // Tell SIPSorcery to substitute Contact URI hosts with our server host for NAT traversal
                _sipTransport.ContactHost = host;

                _sipTransport.SIPRequestInTraceEvent  += (_, _, req)  => LogSipReceived(req.ToString());
                _sipTransport.SIPResponseInTraceEvent += (_, _, resp) => LogSipReceived(resp.ToString());
                _sipTransport.SIPRequestOutTraceEvent  += (_, _, req)  => LogSipSent(req.ToString());
                _sipTransport.SIPResponseOutTraceEvent += (_, _, resp) => LogSipSent(resp.ToString());

                // Handle incoming calls (INVITE requests)
                _sipTransport.SIPTransportRequestReceived += OnSipRequestReceived;

                // Use transport=wss in server URI so SIPSorcery routes REGISTER over WebSocket
                _regAgent = new SIPRegistrationUserAgent(
                    _sipTransport, user, pass, $"sip:{user}@{host};transport=wss", 3600);
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
            try
            {
                var topVia = resp.Header.Vias.TopViaHeader;
                var received = topVia?.ReceivedFromIPAddress;
                if (!string.IsNullOrEmpty(received) && IPAddress.TryParse(received, out var pubIp))
                {
                    _publicIp = pubIp;
                    LogRtp($"Public IP from SIP Via received: {_publicIp}");
                }
                else
                {
                    _publicIp = DiscoverPublicIpViaStun();
                }
            }
            catch (Exception ex)
            {
                LogRtp($"Could not extract public IP from Via: {ex.Message}");
                _publicIp = DiscoverPublicIpViaStun();
            }

            RegistrationMessage = $"Registered as {uri.User}@{uri.HostAddress}";
            SetRegistrationState(RegistrationState.Registered);
            Logger.Info($"SIP registered: {uri} (public IP: {_publicIp})");

            // Start SIP OPTIONS keepalive — sends through the WSS channel to keep the
            // WebSocket alive (replaces .NET's built-in ping which FreeSWITCH rejects)
            _sipKeepAliveTimer?.Dispose();
            _sipKeepAliveTimer = new Timer(SendSipKeepAlive, null,
                TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(25));
        }

        private async void SendSipKeepAlive(object? state)
        {
            try
            {
                if (_sipTransport == null || _proxyEp == null || _config.Username == null) return;

                var host = _sipDomain ?? new Uri(_config.SignalingServerUrl!).Host;
                var optionsReq = SIPRequest.GetRequest(
                    SIPMethodsEnum.OPTIONS,
                    SIPURI.ParseSIPURIRelaxed($"sip:{host};transport=wss"),
                    new SIPToHeader(null, SIPURI.ParseSIPURIRelaxed($"sip:{host}"), null),
                    new SIPFromHeader(null, SIPURI.ParseSIPURIRelaxed($"sip:{_config.Username}@{host}"), CallProperties.CreateNewTag()));
                optionsReq.Header.Contact = new List<SIPContactHeader>
                {
                    SIPContactHeader.GetDefaultSIPContactHeader(SIPSchemesEnum.sip)
                };

                await _sipTransport.SendRequestAsync(optionsReq);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "SIP OPTIONS keep-alive failed");
            }
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

        private async void OnWssDisconnected(object? sender, EventArgs e)
        {
            if (_disposed || _isReconnecting) return;

            // Don't auto-reconnect during an active call — it would drop the call
            if (_currentCall != null && _currentCall.State == CallState.Connected)
            {
                Logger.Warn("WebSocket disconnected during active call");
                LogRtp("[WSS] Connection lost during active call");
                return;
            }

            _isReconnecting = true;

            Logger.Warn("WebSocket disconnected — will attempt to reconnect");
            LogRtp("[WSS] Connection lost. Reconnecting...");
            SetRegistrationState(RegistrationState.Registering);
            RegistrationMessage = "Connection lost. Reconnecting...";

            // Wait before reconnecting to avoid tight loops
            await Task.Delay(5000);

            if (_disposed) { _isReconnecting = false; return; }

            try
            {
                TearDown();
                await RegisterAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Reconnect failed");
                LogRtp($"[WSS] Reconnect failed: {ex.Message}");
                RegistrationMessage = "Connection lost. Please re-register manually.";
                SetRegistrationState(RegistrationState.Failed);
            }

            _isReconnecting = false;
        }

        // ── Call management ───────────────────────────────────────────────────────

        public async Task InitiateCallAsync(string remoteParty)
        {
            if (_sipTransport == null || _proxyEp == null)
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

            _userAgent = new SIPUserAgent(_sipTransport, _proxyEp, false, null);
            _userAgent.OnCallHungup      += OnRemoteHangup;
            _userAgent.ClientCallAnswered += OnCallAnswered;
            _userAgent.ClientCallFailed  += OnCallFailed;
            _userAgent.ClientCallRinging += OnCallRinging;
            _userAgent.ClientCallTrying  += (_, _) => Logger.Debug("INVITE 100 Trying");

            _mediaSession = BuildMediaSession();

            await StunBindRtpSocket((NatAwareRtpSession)_mediaSession);

            var callUri = SIPURI.ParseSIPURIRelaxed($"sip:{remoteParty}@{host}");
            Logger.Info($"Calling {callUri}");
            LogRtp("VoIP media session created (RTP/AVP)");

            bool result;
            try
            {
                result = await _userAgent.Call(callUri.ToString(), _config.Username, _config.Password, _mediaSession);
            }
            catch (Exception ex)
            {
                LogRtp($"[CALL EXCEPTION] {ex.GetType().Name}: {ex.Message}");
                LogRtp(ex.StackTrace ?? "(no stack trace)");
                Logger.Error(ex, "Call() threw an exception");
                CleanupMedia();
                _currentCall.ErrorMessage = ex.Message;
                SetCallState(CallState.Failed);
                _currentCall = null;
                return;
            }
            if (!result)
                LogRtp("[CALL] Call() returned false — waiting for callbacks");
        }

        public Task EndCallAsync()
        {
            if (_currentCall == null) return Task.CompletedTask;
            _currentCall.EndTime = DateTime.Now;
            try
            {
                if (_userAgent != null)
                {
                    if (_userAgent.IsCallActive)
                        _userAgent.Hangup();
                    else
                        _userAgent.Cancel();  // Cancel outgoing call in Initiating/Ringing state
                }
            }
            catch (Exception ex) { Logger.Warn(ex, "Error ending call"); }

            CleanupUserAgent();
            CleanupMedia();
            SetCallState(CallState.Ended);
            _currentCall = null;
            _pendingInvite = null;
            return Task.CompletedTask;
        }

        public void HoldCall()
        {
            if (_currentCall == null || _currentCall.State != CallState.Connected)
                throw new InvalidOperationException("No active call to hold.");

            _micMuted = true;
            LogRtp("Call placed on hold — mic muted");
            SetCallState(CallState.OnHold);
            Logger.Info("Call placed on hold");
        }

        public void UnholdCall()
        {
            if (_currentCall == null || _currentCall.State != CallState.OnHold)
                throw new InvalidOperationException("No call on hold to resume.");

            _micMuted = false;
            LogRtp("Call resumed from hold — mic unmuted");
            SetCallState(CallState.Connected);
            Logger.Info("Call resumed from hold");
        }

        public void SendDtmf(byte tone)
        {
            if (_currentCall == null || _currentCall.State != CallState.Connected)
                throw new InvalidOperationException("No active call for DTMF.");

            if (_mediaSession != null)
            {
                var ev = new RTPEvent(tone, false, RTPEvent.DEFAULT_VOLUME, 160, 101);
                _mediaSession.SendDtmfEvent(ev, default);
                LogRtp($"DTMF tone sent: {tone}");
            }
        }

        public void MuteMicrophone()
        {
            _micMuted = true;
            LogRtp("Microphone muted");
        }

        public void UnmuteMicrophone()
        {
            _micMuted = false;
            LogRtp("Microphone unmuted");
        }

        // ── Incoming call handling ──────────────────────────────────────────────

        private SIPRequest? _pendingInvite;

        private async Task OnSipRequestReceived(SIPEndPoint localEp, SIPEndPoint remoteEp, SIPRequest req)
        {
            // Handle CANCEL from the remote caller
            if (req.Method == SIPMethodsEnum.CANCEL)
            {
                await HandleSipCancel(req);
                return;
            }

            // Respond 200 OK to NOTIFY, OPTIONS, etc. to keep the connection alive
            if (req.Method != SIPMethodsEnum.INVITE && req.Method != SIPMethodsEnum.CANCEL)
            {
                var okResp = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.Ok, null);
                await _sipTransport!.SendResponseAsync(okResp);
                return;
            }

            // INVITE retransmission — resend 180 Ringing
            if (_pendingInvite != null && req.Header.CallId == _pendingInvite.Header.CallId)
            {
                var ringing = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.Ringing, null);
                await _sipTransport!.SendResponseAsync(ringing);
                return;
            }

            Logger.Info($"Incoming INVITE from {req.Header.From.FromURI}");
            LogRtp($"Incoming call from {req.Header.From.FromURI}");

            if (HasActiveCall)
            {
                // Busy — reject with 486
                var busyResp = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.BusyHere, null);
                await _sipTransport!.SendResponseAsync(busyResp);
                LogRtp("Rejected incoming call — busy");
                return;
            }

            _pendingInvite = req;

            // Send 180 Ringing
            var ringingResp = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.Ringing, null);
            await _sipTransport!.SendResponseAsync(ringingResp);

            _currentCall = new CallSession
            {
                RemoteParty = req.Header.From.FromURI.User ?? req.Header.From.FromURI.ToString(),
                State = CallState.Ringing,
                StartTime = DateTime.Now
            };
            SetCallState(CallState.Ringing);

            try { IncomingCall?.Invoke(this, _currentCall); }
            catch (Exception ex) { Logger.Warn(ex, "Error in IncomingCall event handler"); }
        }

        private async Task HandleSipCancel(SIPRequest cancelReq)
        {
            // Always respond 200 OK to CANCEL
            var okResp = SIPResponse.GetResponse(cancelReq, SIPResponseStatusCodesEnum.Ok, null);
            await _sipTransport!.SendResponseAsync(okResp);

            if (_pendingInvite != null && cancelReq.Header.CallId == _pendingInvite.Header.CallId)
            {
                // Send 487 Request Terminated for the original INVITE
                var terminatedResp = SIPResponse.GetResponse(
                    _pendingInvite, SIPResponseStatusCodesEnum.RequestTerminated, null);
                await _sipTransport!.SendResponseAsync(terminatedResp);

                Logger.Info("Incoming call canceled by caller");
                LogRtp("Incoming call canceled by caller");

                if (_currentCall != null)
                {
                    _currentCall.EndTime = DateTime.Now;
                    SetCallState(CallState.Ended);
                    _currentCall = null;
                }
                _pendingInvite = null;

                try { IncomingCallCanceled?.Invoke(this, EventArgs.Empty); }
                catch (Exception ex) { Logger.Warn(ex, "Error in IncomingCallCanceled handler"); }
            }
        }

        public async Task AnswerCallAsync()
        {
            if (_pendingInvite == null || _currentCall == null || _currentCall.State != CallState.Ringing)
                throw new InvalidOperationException("No incoming call to answer.");

            try
            {
                _userAgent = new SIPUserAgent(_sipTransport!, _proxyEp, false, null);
                _userAgent.OnCallHungup += OnRemoteHangup;

                _mediaSession = BuildMediaSession();
                await StunBindRtpSocket((NatAwareRtpSession)_mediaSession);

                var uas = _userAgent.AcceptCall(_pendingInvite);
                await _userAgent.Answer(uas, _mediaSession);

                _rtpPacketsSent = 0;
                _rtpPacketsReceived = 0;
                _rtpBytesSent = 0;
                _rtpBytesReceived = 0;
                _firstRtpSent = null;
                _firstRtpReceived = null;

                await StartAudioAsync();
                StartRtpStatsTimer();
                SetCallState(CallState.Connected);

                _pendingInvite = null;
                LogRtp("Incoming call answered — audio active");
                Logger.Info("Incoming call answered");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error answering incoming call");
                LogRtp($"Answer failed: {ex.Message}");
                CleanupMedia();
                SetCallState(CallState.Failed);
                _currentCall = null;
                _pendingInvite = null;
                throw;
            }
        }

        public void RejectCall()
        {
            if (_pendingInvite == null) return;

            try
            {
                var declineResp = SIPResponse.GetResponse(_pendingInvite, SIPResponseStatusCodesEnum.Decline, null);
                _sipTransport?.SendResponseAsync(declineResp).Wait(2000);
                LogRtp("Incoming call rejected");
                Logger.Info("Incoming call rejected");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Error rejecting call");
            }

            if (_currentCall != null)
            {
                _currentCall.EndTime = DateTime.Now;
                SetCallState(CallState.Ended);
                _currentCall = null;
            }
            _pendingInvite = null;
        }

        // ── SIPUserAgent call-state callbacks ─────────────────────────────────────

        private void OnCallRinging(ISIPClientUserAgent uac, SIPResponse resp)
        {
            if (_currentCall == null) return;  // Stale callback after cancel
            Logger.Info("Call ringing");
            SetCallState(CallState.Ringing);
        }

        private async void OnCallAnswered(ISIPClientUserAgent uac, SIPResponse resp)
        {
            if (_currentCall == null) { Logger.Debug("Ignoring stale OnCallAnswered callback"); return; }
            Logger.Info("Call answered — starting RTP session and audio");

            _rtpPacketsSent = 0;
            _rtpPacketsReceived = 0;
            _rtpBytesSent = 0;
            _rtpBytesReceived = 0;
            _firstRtpSent = null;
            _firstRtpReceived = null;

            try
            {
                if (_mediaSession != null)
                {
                    await _mediaSession.Start();
                    LogRtp($"RTP session started — IsStarted={_mediaSession.IsStarted}");
                }
            }
            catch (Exception ex)
            {
                LogRtp($"RTP session start error: {ex.Message}");
                Logger.Warn(ex, "Failed to start RTP session");
            }

            if (_mediaSession != null)
            {
                var audioDest = _mediaSession.AudioDestinationEndPoint;
                var rtpChannel = _mediaSession.AudioStream?.GetRTPChannel();
                LogRtp($"RTP local endpoint: {rtpChannel?.RTPLocalEndPoint}");
                LogRtp($"RTP remote endpoint: {audioDest}");
                LogRtp($"RTP AcceptFromAny: {_mediaSession.AcceptRtpFromAny}");

                try
                {
                    var silence = new byte[160];
                    Array.Fill(silence, (byte)0xFF);
                    for (int i = 0; i < 5; i++)
                        _mediaSession.SendAudio(160, silence);
                    LogRtp($"Sent 5 NAT-pinhole silence packets to {audioDest}");
                }
                catch (Exception ex)
                {
                    LogRtp($"NAT-pinhole send failed: {ex.Message}");
                }
            }

            await StartAudioAsync();
            StartRtpStatsTimer();
            SetCallState(CallState.Connected);
            LogRtp("Call connected — audio streams active (mic → RTP, RTP → speaker)");
        }

        private void OnCallFailed(ISIPClientUserAgent uac, string reason, SIPResponse? failResponse)
        {
            LastCallFailureReason = reason;
            var statusCode = failResponse != null ? $" ({(int)failResponse.Status} {failResponse.ReasonPhrase})" : "";
            Logger.Warn($"Call failed: {reason}{statusCode}");
            LogRtp($"[CALL FAILED] {reason}{statusCode}");
            if (_currentCall != null)
            {
                _currentCall.ErrorMessage = reason;
                CleanupUserAgent();
                CleanupMedia();
                SetCallState(CallState.Failed);
                _currentCall = null;
            }
            else
            {
                // Stale callback from a previously canceled call — ignore
                Logger.Debug("Ignoring stale OnCallFailed callback (no current call)");
            }
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

        // ── NAT-aware RTP session ────────────────────────────────────────────────

        private class NatAwareRtpSession : RTPSession
        {
            private IPAddress _announcedAddress;
            private int _announcedPort;

            public NatAwareRtpSession(IPAddress announcedAddress, IPAddress bindAddress)
                : base(false, false, false, bindAddress)
            {
                _announcedAddress = announcedAddress;
            }

            public void UpdateAnnouncedEndpoint(IPAddress address, int port)
            {
                _announcedAddress = address;
                _announcedPort = port;
            }

            public override SDP CreateOffer(IPAddress connectionAddress)
            {
                var addr = _announcedAddress ?? connectionAddress;
                var sdp = base.CreateOffer(addr);
                try { sdp.AddressOrHost = addr.ToString(); } catch { }

                if (_announcedPort > 0)
                {
                    try
                    {
                        foreach (var media in sdp.Media)
                            media.Port = _announcedPort;
                    }
                    catch { }
                }

                return sdp;
            }
        }

        private IPAddress? DiscoverPublicIpViaStun()
        {
            try
            {
                var ip = STUNClient.GetPublicIPAddress("stun.l.google.com", 19302);
                if (ip != null)
                {
                    LogRtp($"STUN discovered public IP: {ip}");
                    return ip;
                }
            }
            catch (Exception ex)
            {
                LogRtp($"STUN query failed: {ex.Message}");
            }
            return null;
        }

        private async Task StunBindRtpSocket(NatAwareRtpSession session)
        {
            try
            {
                var rtpChannel = session.AudioStream?.GetRTPChannel();
                if (rtpChannel == null)
                {
                    LogRtp("STUN-bind: no RTP channel available yet");
                    return;
                }

                var localEp = rtpChannel.RTPLocalEndPoint;
                LogRtp($"STUN-bind: local RTP socket {localEp}");

                var stunAddrs = await Dns.GetHostAddressesAsync("stun.l.google.com");
                var stunIp = stunAddrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (stunIp == null)
                {
                    LogRtp("STUN-bind: could not resolve stun.l.google.com");
                    return;
                }

                var stunEp = new IPEndPoint(stunIp, 19302);

                var stunReq = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
                var reqBytes = stunReq.ToByteBuffer(null, false);

                rtpChannel.Send(RTPChannelSocketsEnum.RTP, stunEp, reqBytes);
                LogRtp($"STUN-bind: sent binding request to {stunEp} from port {rtpChannel.RTPPort}");

                var rtpSocket = rtpChannel.RtpSocket;
                var prevTimeout = rtpSocket.ReceiveTimeout;
                rtpSocket.ReceiveTimeout = 3000;

                IPEndPoint? mapped = null;
                var recvBuf = new byte[512];
                EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        var bytesRead = rtpSocket.ReceiveFrom(recvBuf, ref remoteEp);
                        if (bytesRead > 0)
                        {
                            var stunResp = STUNMessage.ParseSTUNMessage(recvBuf, bytesRead);
                            if (stunResp?.Header?.MessageType == STUNMessageTypesEnum.BindingSuccessResponse)
                            {
                                foreach (var attr in stunResp.Attributes)
                                {
                                    if (attr is STUNXORAddressAttribute xor)
                                    {
                                        mapped = new IPEndPoint(xor.Address, xor.Port);
                                        break;
                                    }
                                    if (attr is STUNAddressAttribute addr &&
                                        addr.AttributeType == STUNAttributeTypesEnum.MappedAddress)
                                    {
                                        mapped = new IPEndPoint(addr.Address, addr.Port);
                                    }
                                }
                                break;
                            }
                        }
                    }
                    catch (SocketException) { break; }
                }

                rtpSocket.ReceiveTimeout = prevTimeout;

                if (mapped != null)
                {
                    _publicIp = mapped.Address;
                    session.UpdateAnnouncedEndpoint(mapped.Address, mapped.Port);
                    LogRtp($"STUN-bind: NAT mapped RTP → {mapped} (local {localEp})");
                }
                else
                {
                    LogRtp("STUN-bind: no STUN response, falling back to Via IP");
                    if (_publicIp != null)
                        session.UpdateAnnouncedEndpoint(_publicIp, 0);
                }
            }
            catch (Exception ex)
            {
                LogRtp($"STUN-bind failed: {ex.Message}");
                Logger.Warn(ex, "STUN bind from RTP socket failed");
                if (_publicIp != null)
                    session.UpdateAnnouncedEndpoint(_publicIp, 0);
            }
        }

        // ── Media session (plain RTP/AVP — compatible with standard SIP proxies) ──────

        private RTPSession BuildMediaSession()
        {
            var announceIp = _publicIp ?? _localIp ?? IPAddress.Any;
            var bindIp = _localIp ?? IPAddress.Any;

            LogRtp($"Building media session — bind: {bindIp}, announce (SDP): {announceIp}");

            var session = new NatAwareRtpSession(announceIp, bindIp);
            session.AcceptRtpFromAny = true;

            // Create platform-abstracted audio endpoints
            _audioSource = _audioFactory.CreateAudioSource();
            _audioSink   = _audioFactory.CreateAudioSink();

            _audioSource.RestrictFormats(f => f.Codec == AudioCodecsEnum.PCMU ||
                                              f.Codec == AudioCodecsEnum.PCMA);

            var audioTrack = new MediaStreamTrack(
                _audioSource.GetAudioSourceFormats(),
                MediaStreamStatusEnum.SendRecv);
            session.addTrack(audioTrack);

            // Wrap SendAudio to count outgoing packets + mic level
            _audioSource.OnAudioSourceEncodedSample += (durationRtpUnits, sample) =>
            {
                if (_micMuted) return; // Skip sending when muted/on-hold
                session.SendAudio(durationRtpUnits, sample);
                var count = Interlocked.Increment(ref _rtpPacketsSent);
                Interlocked.Add(ref _rtpBytesSent, sample.Length);
                if (count == 1)
                {
                    _firstRtpSent = DateTime.Now;
                    LogRtp($"First RTP packet SENT ({sample.Length} bytes)");
                }
                var now = DateTime.UtcNow;
                if ((now - _lastMicLevelTime).TotalMilliseconds >= LevelUpdateMs)
                {
                    _lastMicLevelTime = now;
                    MicLevelChanged?.Invoke(this, CalculateAudioLevel(sample));
                }
            };

            session.OnAudioFormatsNegotiated += formats =>
            {
                var fmt = formats.First();
                _audioSource?.SetAudioSourceFormat(fmt);
                _audioSink?.SetAudioSinkFormat(fmt);
                LogRtp($"Audio codec negotiated: {fmt.Codec} PT={fmt.FormatID}");
            };

            session.OnRtpPacketReceived += (ep, media, pkt) =>
            {
                if (media == SDPMediaTypesEnum.audio)
                {
                    try
                    {
                        _audioSink?.GotAudioRtp(ep,
                            pkt.Header.SyncSource,
                            pkt.Header.SequenceNumber,
                            pkt.Header.Timestamp,
                            pkt.Header.PayloadType,
                            pkt.Header.MarkerBit == 1,
                            pkt.Payload);
                    }
                    catch (Exception ex)
                    {
                        var cnt = Interlocked.Read(ref _rtpPacketsReceived);
                        if (cnt < 3)
                            LogRtp($"GotAudioRtp error: {ex.Message}");
                    }

                    var count = Interlocked.Increment(ref _rtpPacketsReceived);
                    Interlocked.Add(ref _rtpBytesReceived, pkt.Payload.Length);
                    if (count == 1)
                    {
                        _firstRtpReceived = DateTime.Now;
                        LogRtp($"First RTP packet RECEIVED from {ep} ({pkt.Payload.Length} bytes, PT={pkt.Header.PayloadType}, SSRC={pkt.Header.SyncSource})");
                    }
                    var now = DateTime.UtcNow;
                    if ((now - _lastSpkLevelTime).TotalMilliseconds >= LevelUpdateMs)
                    {
                        _lastSpkLevelTime = now;
                        SpeakerLevelChanged?.Invoke(this, CalculateAudioLevel(pkt.Payload));
                    }
                }
            };

            return session;
        }

        private async Task StartAudioAsync()
        {
            try
            {
                if (_audioSink != null)
                {
                    await _audioSink.StartAudioSink();
                    LogRtp("Audio sink (speaker) started");
                }
                if (_audioSource != null)
                {
                    await _audioSource.StartAudio();
                    LogRtp("Audio source (mic) started");
                }
            }
            catch (Exception ex)
            {
                LogRtp($"Audio start error: {ex.Message}");
                Logger.Warn(ex, "Failed to start audio");
            }
        }

        private int _rtpStatsTickCount;

        private void StartRtpStatsTimer()
        {
            _rtpStatsTickCount = 0;
            _rtpStatsTimer?.Dispose();
            _rtpStatsTimer = new Timer(_ =>
            {
                if (_currentCall?.State == CallState.Connected)
                {
                    _rtpStatsTickCount++;
                    var sent = Interlocked.Read(ref _rtpPacketsSent);
                    var recv = Interlocked.Read(ref _rtpPacketsReceived);
                    var bytesSent = Interlocked.Read(ref _rtpBytesSent);
                    var bytesRecv = Interlocked.Read(ref _rtpBytesReceived);
                    LogRtp($"RTP stats — Sent: {sent} pkts ({bytesSent / 1024}KB) | Recv: {recv} pkts ({bytesRecv / 1024}KB)");

                    if (_rtpStatsTickCount == 1 && recv == 0 && sent > 0)
                        LogRtp("WARNING: Sending RTP but receiving NONE — possible NAT/firewall issue");
                    if (_rtpStatsTickCount == 1 && sent == 0)
                        LogRtp("WARNING: No RTP sent — microphone may not be working");
                }
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
        }

        private void StopRtpStatsTimer()
        {
            _rtpStatsTimer?.Dispose();
            _rtpStatsTimer = null;
        }

        private void CleanupUserAgent()
        {
            if (_userAgent != null)
            {
                _userAgent.OnCallHungup      -= OnRemoteHangup;
                _userAgent.ClientCallAnswered -= OnCallAnswered;
                _userAgent.ClientCallFailed  -= OnCallFailed;
                _userAgent.ClientCallRinging -= OnCallRinging;
                _userAgent = null;
            }
        }

        private void CleanupMedia()
        {
            StopRtpStatsTimer();

            var sent = Interlocked.Read(ref _rtpPacketsSent);
            var recv = Interlocked.Read(ref _rtpPacketsReceived);
            if (sent > 0 || recv > 0)
                LogRtp($"RTP final stats — Sent: {sent} pkts | Recv: {recv} pkts");

            if (_audioSink != null)
            {
                try { _audioSink.CloseAudioSink().Wait(500); } catch { }
                if (_audioSink is IDisposable sinkDisp) try { sinkDisp.Dispose(); } catch { }
                _audioSink = null;
            }

            if (_audioSource != null)
            {
                try { _audioSource.CloseAudio().Wait(500); } catch { }
                if (_audioSource is IDisposable srcDisp) try { srcDisp.Dispose(); } catch { }
                _audioSource = null;
                LogRtp("Audio endpoint closed");
            }

            if (_mediaSession != null)
            {
                try { _mediaSession.Close("call ended"); } catch { }
                _mediaSession = null;
            }
        }

        // ── State helpers ─────────────────────────────────────────────────────────

        public CallSession?  GetCurrentCall() => _currentCall;
        public IPAddress?    GetPublicIp()    => _publicIp;

        public bool HasActiveCall =>
            _currentCall != null &&
            (_currentCall.State == CallState.Initiating ||
             _currentCall.State == CallState.Ringing ||
             _currentCall.State == CallState.Connected ||
             _currentCall.State == CallState.OnHold);

        public bool IsRegistered => RegistrationState == RegistrationState.Registered;

        public void Unregister()
        {
            if (_regAgent != null)
            {
                try
                {
                    _regAgent.Stop();
                    LogRtp("SIP unregistered on exit");
                    Logger.Info("SIP unregistered");
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Error during unregister");
                }
                _regAgent = null;
                SetRegistrationState(RegistrationState.Unregistered);
            }
        }

        public (long sent, long recv, long bytesSent, long bytesRecv) GetRtpStats()
            => (Interlocked.Read(ref _rtpPacketsSent),
                Interlocked.Read(ref _rtpPacketsReceived),
                Interlocked.Read(ref _rtpBytesSent),
                Interlocked.Read(ref _rtpBytesReceived));

        public TimeSpan GetCallDuration()
        {
            if (_currentCall == null || _currentCall.State != CallState.Connected)
                return TimeSpan.Zero;
            return DateTime.Now - _callStartTime;
        }

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
            var previousState = _currentCall?.State ?? CallState.Idle;
            if (_currentCall != null)
                _currentCall.State = state;
            if (state == CallState.Connected)
                _callStartTime = DateTime.Now;

            CallStateChanged?.Invoke(this, state);

            // Fire rich event with full context
            if (_currentCall != null)
            {
                CallStateDetailChanged?.Invoke(this,
                    new CallStateChangedEventArgs(previousState, state, _currentCall,
                        state == CallState.Failed ? LastCallFailureReason : null));
            }
        }

        // ── Logging helpers ───────────────────────────────────────────────────────

        private void LogSipSent(string message)     => LogSip(">>", message);
        private void LogSipReceived(string message)  => LogSip("<<", message);

        private void LogSip(string direction, string message)
        {
            var entry = new SipLogEventArgs(direction, message);
            lock (_sipLogBuffer)
            {
                _sipLogBuffer.Enqueue(entry);
                while (_sipLogBuffer.Count > SipLogBufferMax) _sipLogBuffer.Dequeue();
            }
            Logger.Debug($"SIP {(direction == ">>" ? "SEND" : "RECV")}: {FirstLine(message)}");
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

        private static float CalculateAudioLevel(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return 0f;
            int peak = 0;
            foreach (byte b in payload)
            {
                byte inv = (byte)~b;
                int exp = (inv >> 4) & 0x07;
                int man = inv & 0x0F;
                int mag = ((man << 1) + 33) << exp;
                if (mag > peak) peak = mag;
            }
            return Math.Min(peak / 8031.0f, 1.0f);
        }

        private static string FirstLine(string msg)
        {
            var i = msg.IndexOf('\n');
            return (i < 0 ? msg : msg[..i]).Trim();
        }

        // ── Teardown ──────────────────────────────────────────────────────────────

        private void TearDown()
        {
            _sipKeepAliveTimer?.Dispose();
            _sipKeepAliveTimer = null;

            try { _regAgent?.Stop(); } catch { }
            _regAgent = null;

            CleanupMedia();

            try { if (_userAgent?.IsCallActive == true) _userAgent.Hangup(); } catch { }
            _userAgent = null;
            _proxyEp   = null;
            _localIp   = null;

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
