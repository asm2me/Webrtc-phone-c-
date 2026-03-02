using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebRtcPhoneDialer.Models;

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

    public class WebRtcService : IDisposable
    {
        private CallSession? _currentCall;
        private WebRtcConfiguration _config;
        private DateTime _callStartTime;
        private bool _disposed = false;
        private ClientWebSocket? _signalingSocket;
        private CancellationTokenSource? _registrationCts;

        // SIP domain override (Settings > SIP Domain field)
        private string? _sipDomain;

        // Circular buffer of last 200 SIP messages for Debug window replay
        private readonly Queue<SipLogEventArgs> _sipLogBuffer = new();
        private const int SipLogBufferMax = 200;

        // Active call signaling state
        private string? _activeCallId;
        private string? _activeCallFromTag;
        private string? _activeCallToTag;
        private string? _activeCallHost;
        private string? _activeCallRemote;
        private string? _activeCallUsername;
        private string? _activeCallBranch;
        private int _activeCallCSeq;

        public RegistrationState RegistrationState { get; private set; } = RegistrationState.Unregistered;
        public string RegistrationMessage { get; private set; } = "Not registered";
        public string? LastCallFailureReason { get; private set; }

        public event EventHandler<RegistrationState>? RegistrationStateChanged;
        public event EventHandler<CallState>?         CallStateChanged;
        public event EventHandler<SipLogEventArgs>?   SipMessageLogged;

        public IEnumerable<SipLogEventArgs> GetSipLogHistory()
        {
            lock (_sipLogBuffer)
                return _sipLogBuffer.ToArray();
        }

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public WebRtcService()
        {
            _config = new WebRtcConfiguration();
            InitializeDefaultConfiguration();
            Logger.Info("WebRTC Service initialized");
        }

        private void InitializeDefaultConfiguration()
        {
            _config.IceServers = new List<IceServer>
            {
                new IceServer { Url = "stun:stun.l.google.com:19302" },
                new IceServer { Url = "stun:stun1.l.google.com:19302" }
            };
            _config.StunServer  = "stun:stun.l.google.com:19302";
            _config.EnableAudio = true;
            _config.EnableVideo = false;
        }

        // ── Call management ───────────────────────────────────────────────────────

        public async Task InitiateCallAsync(string remoteParty)
        {
            if (_signalingSocket?.State != WebSocketState.Open)
                throw new InvalidOperationException("Not connected. Please register first.");

            if (_currentCall != null &&
                (_currentCall.State == CallState.Initiating ||
                 _currentCall.State == CallState.Ringing    ||
                 _currentCall.State == CallState.Connected))
                throw new InvalidOperationException("A call is already active. Please hang up first.");

            if (string.IsNullOrEmpty(_config.SignalingServerUrl))
                throw new InvalidOperationException("Signaling server URL not configured.");

            var host     = _sipDomain ?? new Uri(_config.SignalingServerUrl).Host;
            var username = _config.Username ?? "";

            LastCallFailureReason = null;
            _activeCallId       = Guid.NewGuid().ToString("N");
            _activeCallFromTag  = Guid.NewGuid().ToString("N").Substring(0, 8);
            _activeCallToTag    = null;
            _activeCallHost     = host;
            _activeCallRemote   = remoteParty;
            _activeCallUsername = username;
            _activeCallBranch   = "z9hG4bK" + Guid.NewGuid().ToString("N").Substring(0, 16);
            _activeCallCSeq     = 1;

            _currentCall = new CallSession
            {
                RemoteParty = remoteParty,
                State       = CallState.Initiating,
                StartTime   = DateTime.Now
            };

            SetCallState(CallState.Initiating);

            var sdp    = GenerateSdpOffer();
            var invite = BuildInvite(sdp);
            await SendSipAsync(invite);
            Logger.Info($"INVITE sent to {remoteParty}@{host}");
        }

        public async Task EndCallAsync()
        {
            if (_currentCall == null) return;

            var state = _currentCall.State;
            _currentCall.EndTime = DateTime.Now;

            try
            {
                if (state == CallState.Connected)
                    await SendByeAsync();
                else if (state == CallState.Initiating || state == CallState.Ringing)
                    await SendCancelAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Error sending BYE/CANCEL");
            }

            SetCallState(CallState.Ended);
            _currentCall = null;
        }

        public TimeSpan GetCallDuration()
        {
            if (_currentCall == null || _currentCall.State != CallState.Connected)
                return TimeSpan.Zero;
            return DateTime.Now - _callStartTime;
        }

        public CallSession? GetCurrentCall() => _currentCall;

        // ── Configuration ─────────────────────────────────────────────────────────

        public void ConfigureServers(string stunServer, string[] iceServers)
        {
            try
            {
                _config.StunServer = stunServer;
                _config.IceServers.Clear();
                foreach (var server in iceServers)
                {
                    var url = server.Trim();
                    if (!string.IsNullOrEmpty(url))
                        _config.IceServers.Add(new IceServer { Url = url });
                }
                Logger.Info($"WebRTC configuration updated with {_config.IceServers.Count} ICE servers");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error configuring servers");
                throw;
            }
        }

        public WebRtcConfiguration GetConfiguration() => _config;

        public void Configure(AppSettings settings)
        {
            try
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
                        _config.IceServers.Add(new IceServer { Url = url });
                }

                Logger.Info($"Configuration applied: STUN={settings.StunServer}, Audio={settings.AudioCodecName}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error applying settings");
                throw;
            }
        }

        // ── State helpers ─────────────────────────────────────────────────────────

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

        private void LogSipSent(string message)
        {
            var entry = new SipLogEventArgs(">>", message);
            BufferSipEntry(entry);
            Logger.Debug($"SIP SEND: {FirstLine(message)}");
            SipMessageLogged?.Invoke(this, entry);
        }

        private void LogSipReceived(string message)
        {
            var entry = new SipLogEventArgs("<<", message);
            BufferSipEntry(entry);
            Logger.Debug($"SIP RECV: {FirstLine(message)}");
            SipMessageLogged?.Invoke(this, entry);
        }

        private void BufferSipEntry(SipLogEventArgs entry)
        {
            lock (_sipLogBuffer)
            {
                _sipLogBuffer.Enqueue(entry);
                while (_sipLogBuffer.Count > SipLogBufferMax)
                    _sipLogBuffer.Dequeue();
            }
        }

        // ── Registration ──────────────────────────────────────────────────────────

        public async Task RegisterAsync()
        {
            if (string.IsNullOrEmpty(_config.SignalingServerUrl))
            {
                RegistrationMessage = "Signaling server URL not configured. Open Settings and enter the Signaling URL.";
                SetRegistrationState(RegistrationState.Failed);
                return;
            }

            _registrationCts?.Cancel();
            _registrationCts?.Dispose();
            _registrationCts = new CancellationTokenSource();

            try { _signalingSocket?.Dispose(); } catch { }
            _signalingSocket = null;

            SetRegistrationState(RegistrationState.Registering);
            RegistrationMessage = "Connecting...";

            try
            {
                _signalingSocket = new ClientWebSocket();
                _signalingSocket.Options.AddSubProtocol("sip");

                var uri = new Uri(_config.SignalingServerUrl);
                await _signalingSocket.ConnectAsync(uri, _registrationCts.Token);
                Logger.Info($"WebSocket connected: {_config.SignalingServerUrl}");

                var host     = _sipDomain ?? uri.Host;
                var username = _config.Username ?? "";
                var password = _config.Password ?? "";
                var callId   = Guid.NewGuid().ToString("N");
                var tag      = Guid.NewGuid().ToString("N").Substring(0, 8);
                var branch   = "z9hG4bK" + Guid.NewGuid().ToString("N").Substring(0, 16);
                const string localHost = "sip-client.invalid";

                // Step 1: unauthenticated REGISTER
                await SendSipAsync(BuildRegister(username, host, localHost, callId, tag, branch, 1, null));
                var resp1 = await ReceiveSipAsync();

                if (resp1.StartsWith("SIP/2.0 200"))
                {
                    RegistrationMessage = $"Registered as {username}@{host}";
                    SetRegistrationState(RegistrationState.Registered);
                    _ = Task.Run(() => SipKeepAliveLoopAsync(_registrationCts.Token));
                    return;
                }

                if (resp1.StartsWith("SIP/2.0 401") || resp1.StartsWith("SIP/2.0 407"))
                {
                    var headerName = resp1.StartsWith("SIP/2.0 401") ? "WWW-Authenticate" : "Proxy-Authenticate";
                    var wwwAuth    = ExtractSipHeader(resp1, headerName);
                    if (wwwAuth == null)
                    {
                        RegistrationMessage = "Missing auth challenge header";
                        SetRegistrationState(RegistrationState.Failed);
                        return;
                    }

                    var branch2   = "z9hG4bK" + Guid.NewGuid().ToString("N").Substring(0, 16);
                    var authValue = BuildDigestAuth(wwwAuth, username, password, host, "REGISTER");
                    await SendSipAsync(BuildRegister(username, host, localHost, callId, tag, branch2, 2, authValue));
                    var resp2 = await ReceiveSipAsync();

                    if (resp2.StartsWith("SIP/2.0 200"))
                    {
                        RegistrationMessage = $"Registered as {username}@{host}";
                        SetRegistrationState(RegistrationState.Registered);
                        _ = Task.Run(() => SipKeepAliveLoopAsync(_registrationCts.Token));
                    }
                    else
                    {
                        RegistrationMessage = $"Registration failed: {FirstLine(resp2)}";
                        SetRegistrationState(RegistrationState.Failed);
                    }
                    return;
                }

                RegistrationMessage = $"Unexpected response: {FirstLine(resp1)}";
                SetRegistrationState(RegistrationState.Failed);
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Registration cancelled");
                RegistrationMessage = "Registration cancelled";
                SetRegistrationState(RegistrationState.Unregistered);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Registration failed");
                RegistrationMessage = $"Error: {ex.Message}";
                SetRegistrationState(RegistrationState.Failed);
            }
        }

        // ── SIP keep-alive + inbound message dispatch ─────────────────────────────

        private async Task SipKeepAliveLoopAsync(CancellationToken ct)
        {
            Logger.Info("SIP keep-alive loop started");
            try
            {
                var buffer = new byte[65536];
                while (!ct.IsCancellationRequested && _signalingSocket?.State == WebSocketState.Open)
                {
                    var result = await _signalingSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    if (string.IsNullOrWhiteSpace(msg)) continue;

                    LogSipReceived(msg);

                    if (msg.StartsWith("OPTIONS"))
                        await SendOptionsResponseAsync(msg, ct);
                    else if (msg.StartsWith("BYE"))
                        await HandleRemoteByeAsync(msg);
                    else if (msg.StartsWith("SIP/2.0"))
                        await HandleSipResponseAsync(msg);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Logger.Warn(ex, "SIP keep-alive ended"); }

            if (!ct.IsCancellationRequested && RegistrationState == RegistrationState.Registered)
            {
                RegistrationMessage = "Connection lost — click Register to reconnect";
                SetRegistrationState(RegistrationState.Failed);
            }
            Logger.Info("SIP keep-alive loop ended");
        }

        private async Task HandleSipResponseAsync(string msg)
        {
            var callId = ExtractSipHeader(msg, "Call-ID");
            if (_activeCallId == null || callId != _activeCallId || _currentCall == null)
                return;

            if (_currentCall.State == CallState.Ended)
                return;

            var first = FirstLine(msg);

            if (first.Contains(" 100 "))
            {
                Logger.Info("100 Trying");
            }
            else if (first.Contains(" 180 ") || first.Contains(" 183 "))
            {
                SetCallState(CallState.Ringing);
            }
            else if (first.Contains(" 200 "))
            {
                var toHeader = ExtractSipHeader(msg, "To");
                if (toHeader != null)
                    _activeCallToTag = ExtractTagFromHeader(toHeader);

                await SendAckAsync();
                SetCallState(CallState.Connected);
            }
            else if (first.Contains(" 401 ") || first.Contains(" 407 "))
            {
                // Asterisk challenges the INVITE — re-send with digest auth
                var headerName = first.Contains(" 401 ") ? "WWW-Authenticate" : "Proxy-Authenticate";
                var wwwAuth    = ExtractSipHeader(msg, headerName);

                if (wwwAuth == null || _activeCallHost == null || _activeCallUsername == null)
                {
                    LastCallFailureReason = "Auth challenge missing header";
                    SetCallState(CallState.Failed);
                    _currentCall = null;
                    return;
                }

                var inviteUri = $"sip:{_activeCallRemote}@{_activeCallHost}";
                var authValue = BuildDigestAuth(wwwAuth, _activeCallUsername,
                    _config.Password ?? "", _activeCallHost, "INVITE", inviteUri);

                // New branch + incremented CSeq for re-sent INVITE
                _activeCallBranch = "z9hG4bK" + Guid.NewGuid().ToString("N").Substring(0, 16);
                _activeCallCSeq++;

                var sdp    = GenerateSdpOffer();
                var invite = BuildInvite(sdp, authValue);
                await SendSipAsync(invite);
                Logger.Info("Re-sent INVITE with digest auth");
            }
            else
            {
                LastCallFailureReason = first;
                Logger.Warn($"Call failed/rejected: {first}");
                if (_currentCall != null)
                    _currentCall.ErrorMessage = first;
                SetCallState(CallState.Failed);
                _currentCall = null;
            }
        }

        private async Task HandleRemoteByeAsync(string msg)
        {
            var sb     = new StringBuilder("SIP/2.0 200 OK\r\n");
            var via    = ExtractSipHeader(msg, "Via");
            var from   = ExtractSipHeader(msg, "From");
            var to     = ExtractSipHeader(msg, "To");
            var callId = ExtractSipHeader(msg, "Call-ID");
            var cseq   = ExtractSipHeader(msg, "CSeq");

            if (via    != null) sb.Append($"Via: {via}\r\n");
            if (from   != null) sb.Append($"From: {from}\r\n");
            if (to     != null) sb.Append($"To: {to}\r\n");
            if (callId != null) sb.Append($"Call-ID: {callId}\r\n");
            if (cseq   != null) sb.Append($"CSeq: {cseq}\r\n");
            sb.Append("Content-Length: 0\r\n\r\n");

            await SendRawAsync(sb.ToString());
            Logger.Info("Remote BYE acknowledged");

            if (_currentCall != null)
            {
                _currentCall.EndTime = DateTime.Now;
                SetCallState(CallState.Ended);
                _currentCall = null;
            }
        }

        // ── SIP message builders ──────────────────────────────────────────────────

        private string BuildInvite(string sdp, string? authorization = null)
        {
            var sdpLen = Encoding.UTF8.GetByteCount(sdp);
            var sb = new StringBuilder();
            sb.Append($"INVITE sip:{_activeCallRemote}@{_activeCallHost} SIP/2.0\r\n");
            sb.Append($"Via: SIP/2.0/WSS sip-client.invalid;branch={_activeCallBranch}\r\n");
            sb.Append("Max-Forwards: 70\r\n");
            sb.Append($"To: <sip:{_activeCallRemote}@{_activeCallHost}>\r\n");
            sb.Append($"From: <sip:{_activeCallUsername}@{_activeCallHost}>;tag={_activeCallFromTag}\r\n");
            sb.Append($"Call-ID: {_activeCallId}\r\n");
            sb.Append($"CSeq: {_activeCallCSeq} INVITE\r\n");
            sb.Append($"Contact: <sip:{_activeCallUsername}@sip-client.invalid;transport=wss>\r\n");
            sb.Append("Allow: INVITE, ACK, BYE, CANCEL, OPTIONS\r\n");
            if (authorization != null)
                sb.Append($"Authorization: {authorization}\r\n");
            sb.Append("Content-Type: application/sdp\r\n");
            sb.Append($"Content-Length: {sdpLen}\r\n");
            sb.Append("\r\n");
            sb.Append(sdp);
            return sb.ToString();
        }

        private async Task SendAckAsync()
        {
            var toTag  = _activeCallToTag != null ? $";tag={_activeCallToTag}" : "";
            var branch = "z9hG4bK" + Guid.NewGuid().ToString("N").Substring(0, 16);
            var sb = new StringBuilder();
            sb.Append($"ACK sip:{_activeCallRemote}@{_activeCallHost} SIP/2.0\r\n");
            sb.Append($"Via: SIP/2.0/WSS sip-client.invalid;branch={branch}\r\n");
            sb.Append("Max-Forwards: 70\r\n");
            sb.Append($"To: <sip:{_activeCallRemote}@{_activeCallHost}>{toTag}\r\n");
            sb.Append($"From: <sip:{_activeCallUsername}@{_activeCallHost}>;tag={_activeCallFromTag}\r\n");
            sb.Append($"Call-ID: {_activeCallId}\r\n");
            sb.Append($"CSeq: {_activeCallCSeq} ACK\r\n");
            sb.Append("Content-Length: 0\r\n\r\n");
            await SendRawAsync(sb.ToString());
            Logger.Info("ACK sent");
        }

        private async Task SendByeAsync()
        {
            var toTag  = _activeCallToTag != null ? $";tag={_activeCallToTag}" : "";
            var branch = "z9hG4bK" + Guid.NewGuid().ToString("N").Substring(0, 16);
            _activeCallCSeq++;
            var sb = new StringBuilder();
            sb.Append($"BYE sip:{_activeCallRemote}@{_activeCallHost} SIP/2.0\r\n");
            sb.Append($"Via: SIP/2.0/WSS sip-client.invalid;branch={branch}\r\n");
            sb.Append("Max-Forwards: 70\r\n");
            sb.Append($"To: <sip:{_activeCallRemote}@{_activeCallHost}>{toTag}\r\n");
            sb.Append($"From: <sip:{_activeCallUsername}@{_activeCallHost}>;tag={_activeCallFromTag}\r\n");
            sb.Append($"Call-ID: {_activeCallId}\r\n");
            sb.Append($"CSeq: {_activeCallCSeq} BYE\r\n");
            sb.Append("Content-Length: 0\r\n\r\n");
            await SendRawAsync(sb.ToString());
            Logger.Info("BYE sent");
        }

        private async Task SendCancelAsync()
        {
            var sb = new StringBuilder();
            sb.Append($"CANCEL sip:{_activeCallRemote}@{_activeCallHost} SIP/2.0\r\n");
            sb.Append($"Via: SIP/2.0/WSS sip-client.invalid;branch={_activeCallBranch}\r\n");
            sb.Append("Max-Forwards: 70\r\n");
            sb.Append($"To: <sip:{_activeCallRemote}@{_activeCallHost}>\r\n");
            sb.Append($"From: <sip:{_activeCallUsername}@{_activeCallHost}>;tag={_activeCallFromTag}\r\n");
            sb.Append($"Call-ID: {_activeCallId}\r\n");
            sb.Append($"CSeq: {_activeCallCSeq} CANCEL\r\n");
            sb.Append("Content-Length: 0\r\n\r\n");
            await SendRawAsync(sb.ToString());
            Logger.Info("CANCEL sent");
        }

        private static string GenerateSdpOffer()
        {
            var ts          = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var ufrag       = Guid.NewGuid().ToString("N").Substring(0, 8);
            var pwd         = Guid.NewGuid().ToString("N").Substring(0, 24);
            var fingerprint = GeneratePlaceholderFingerprint();

            var sb = new StringBuilder();
            sb.Append($"v=0\r\n");
            sb.Append($"o=- {ts} {ts} IN IP4 sip-client.invalid\r\n");
            sb.Append($"s=-\r\n");
            sb.Append($"t=0 0\r\n");
            sb.Append($"a=group:BUNDLE audio\r\n");
            sb.Append($"m=audio 9 UDP/TLS/RTP/SAVPF 111 0 8 101\r\n");
            sb.Append($"c=IN IP4 0.0.0.0\r\n");
            sb.Append($"a=rtcp:9 IN IP4 0.0.0.0\r\n");
            sb.Append($"a=ice-ufrag:{ufrag}\r\n");
            sb.Append($"a=ice-pwd:{pwd}\r\n");
            sb.Append($"a=ice-options:trickle\r\n");
            sb.Append($"a=fingerprint:sha-256 {fingerprint}\r\n");
            sb.Append($"a=setup:actpass\r\n");
            sb.Append($"a=mid:audio\r\n");
            sb.Append($"a=sendrecv\r\n");
            sb.Append($"a=rtcp-mux\r\n");
            sb.Append($"a=rtpmap:111 opus/48000/2\r\n");
            sb.Append($"a=fmtp:111 minptime=10;useinbandfec=1\r\n");
            sb.Append($"a=rtpmap:0 PCMU/8000\r\n");
            sb.Append($"a=rtpmap:8 PCMA/8000\r\n");
            sb.Append($"a=rtpmap:101 telephone-event/8000\r\n");
            sb.Append($"a=fmtp:101 0-16\r\n");
            return sb.ToString();
        }

        private static string GeneratePlaceholderFingerprint()
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Guid.NewGuid().ToByteArray());
            return string.Join(":", hash.Select(b => b.ToString("X2")));
        }

        private static string? ExtractTagFromHeader(string header)
        {
            var i = header.IndexOf(";tag=", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            i += 5;
            var j = header.IndexOf(';', i);
            return j > i ? header.Substring(i, j - i) : header.Substring(i).Trim();
        }

        // ── Low-level SIP helpers ─────────────────────────────────────────────────

        private static string BuildRegister(
            string user, string host, string localHost,
            string callId, string tag, string branch,
            int cseq, string? authorization)
        {
            var sb = new StringBuilder();
            sb.Append($"REGISTER sip:{host} SIP/2.0\r\n");
            sb.Append($"Via: SIP/2.0/WSS {localHost};branch={branch}\r\n");
            sb.Append("Max-Forwards: 70\r\n");
            sb.Append($"To: <sip:{user}@{host}>\r\n");
            sb.Append($"From: <sip:{user}@{host}>;tag={tag}\r\n");
            sb.Append($"Call-ID: {callId}\r\n");
            sb.Append($"CSeq: {cseq} REGISTER\r\n");
            sb.Append($"Contact: <sip:{user}@{localHost};transport=wss>\r\n");
            sb.Append("Expires: 3600\r\n");
            sb.Append("Content-Length: 0\r\n");
            if (authorization != null)
                sb.Append($"Authorization: {authorization}\r\n");
            sb.Append("\r\n");
            return sb.ToString();
        }

        private async Task SendSipAsync(string message)
        {
            LogSipSent(message);
            var bytes = Encoding.UTF8.GetBytes(message);
            await _signalingSocket!.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                _registrationCts!.Token);
        }

        private async Task SendRawAsync(string message)
        {
            if (_signalingSocket?.State != WebSocketState.Open) return;
            LogSipSent(message);
            var bytes = Encoding.UTF8.GetBytes(message);
            await _signalingSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                _registrationCts?.Token ?? CancellationToken.None);
        }

        private async Task<string> ReceiveSipAsync()
        {
            var buffer = new byte[16384];
            var result = await _signalingSocket!.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                _registrationCts!.Token);
            var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
            LogSipReceived(msg);
            return msg;
        }

        private static string? ExtractSipHeader(string message, string headerName)
        {
            foreach (var line in message.Split('\n'))
            {
                if (line.StartsWith(headerName + ":", StringComparison.OrdinalIgnoreCase))
                    return line.Substring(headerName.Length + 1).Trim();
            }
            return null;
        }

        private static string BuildDigestAuth(
            string wwwAuth, string user, string password, string host, string method,
            string? uriOverride = null)
        {
            // Extract quoted param: key="value"
            static string QParam(string src, string key)
            {
                var token = key + "=\"";
                var i = src.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return "";
                i += token.Length;
                var j = src.IndexOf('"', i);
                return j > i ? src.Substring(i, j - i) : "";
            }
            // Extract possibly-unquoted param (e.g. algorithm=MD5, qop="auth")
            static string AnyParam(string src, string key)
            {
                var v = QParam(src, key);
                if (!string.IsNullOrEmpty(v)) return v;
                var token = key + "=";
                var i = src.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return "";
                i += token.Length;
                var end = src.IndexOfAny(new[] { ',', ' ', '\r', '\n' }, i);
                return end >= 0 ? src.Substring(i, end - i).Trim() : src.Substring(i).Trim();
            }

            var realm     = QParam(wwwAuth, "realm");
            var nonce     = QParam(wwwAuth, "nonce");
            var opaque    = QParam(wwwAuth, "opaque");
            var qop       = QParam(wwwAuth, "qop");           // "auth" or "auth,auth-int" or ""
            var algorithm = AnyParam(wwwAuth, "algorithm");
            if (string.IsNullOrEmpty(algorithm)) algorithm = "MD5";

            var uri = uriOverride ?? $"sip:{host}";
            var ha1 = ComputeMd5($"{user}:{realm}:{password}");
            var ha2 = ComputeMd5($"{method}:{uri}");

            var sb = new StringBuilder();
            sb.Append($"Digest username=\"{user}\",realm=\"{realm}\",nonce=\"{nonce}\",uri=\"{uri}\"");

            if (!string.IsNullOrEmpty(qop) && qop.Contains("auth"))
            {
                var cnonce   = Guid.NewGuid().ToString("N").Substring(0, 8);
                const string nc = "00000001";
                var response = ComputeMd5($"{ha1}:{nonce}:{nc}:{cnonce}:auth:{ha2}");
                sb.Append($",response=\"{response}\",algorithm={algorithm}");
                sb.Append($",cnonce=\"{cnonce}\",nc={nc},qop=auth");
            }
            else
            {
                var response = ComputeMd5($"{ha1}:{nonce}:{ha2}");
                sb.Append($",response=\"{response}\",algorithm={algorithm}");
            }

            if (!string.IsNullOrEmpty(opaque))
                sb.Append($",opaque=\"{opaque}\"");

            return sb.ToString();
        }

        private static string ComputeMd5(string input)
        {
            using var md5 = MD5.Create();
            var bytes     = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        private static string FirstLine(string msg) => msg.Split('\n')[0].Trim();

        private async Task SendOptionsResponseAsync(string optionsMsg, CancellationToken ct)
        {
            var sb     = new StringBuilder("SIP/2.0 200 OK\r\n");
            var via    = ExtractSipHeader(optionsMsg, "Via");
            var from   = ExtractSipHeader(optionsMsg, "From");
            var to     = ExtractSipHeader(optionsMsg, "To");
            var callId = ExtractSipHeader(optionsMsg, "Call-ID");
            var cseq   = ExtractSipHeader(optionsMsg, "CSeq");

            if (via    != null) sb.Append($"Via: {via}\r\n");
            if (from   != null) sb.Append($"From: {from}\r\n");
            if (to     != null) sb.Append($"To: {to}\r\n");
            if (callId != null) sb.Append($"Call-ID: {callId}\r\n");
            if (cseq   != null) sb.Append($"CSeq: {cseq}\r\n");
            sb.Append("Allow: INVITE, ACK, BYE, CANCEL, OPTIONS, REGISTER\r\n");
            sb.Append("Content-Length: 0\r\n\r\n");

            await SendRawAsync(sb.ToString());
            Logger.Debug("OPTIONS 200 OK sent");
        }

        private void CreatePeerConnection(string remoteParty)
        {
            Logger.Debug($"Creating peer connection for {remoteParty}");
        }

        private void ClosePeerConnection()
        {
            Logger.Debug("Closing peer connection");
        }

        public void Dispose()
        {
            if (_disposed) return;
            try
            {
                _registrationCts?.Cancel();
                _registrationCts?.Dispose();
                try { _signalingSocket?.Dispose(); } catch { }
                _currentCall = null;
                _disposed    = true;
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
