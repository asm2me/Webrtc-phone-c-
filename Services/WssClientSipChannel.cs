using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.SIP;

namespace WebRtcPhoneDialer.Services
{
    /// <summary>
    /// A client-side WebSocket SIP channel that pre-connects to a full WSS URI
    /// (including path, e.g. /ws) and bypasses TLS certificate validation for
    /// self-signed certificates on FreePBX/Asterisk servers.
    ///
    /// Replaces SIPSorcery's built-in SIPClientWebSocketChannel which hardcodes
    /// the WebSocket URI as wss://ip:port (no path) and does not expose a
    /// certificate validation callback.
    /// </summary>
    internal sealed class WssClientSipChannel : SIPChannel
    {
        private readonly Uri         _serverUri;
        private readonly SIPEndPoint _remoteEp;
        private readonly SIPEndPoint _localEp;   // stored because ListeningSIPEndPoint is base-computed
        private ClientWebSocket?     _ws;
        private CancellationTokenSource? _cts;

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        /// <param name="serverUri">
        ///   Full WebSocket URI including path, e.g. wss://pbx.example.com:8089/ws
        /// </param>
        /// <param name="localSipEp">
        ///   Local SIPEndPoint used for Via / Contact headers (wss, local IP, any port).
        /// </param>
        /// <param name="remoteSipEp">
        ///   SIPEndPoint of the server (used as the remote address for received messages).
        /// </param>
        public WssClientSipChannel(Uri serverUri, SIPEndPoint localSipEp, SIPEndPoint remoteSipEp)
            : base(Encoding.UTF8, Encoding.UTF8)
        {
            _serverUri = serverUri;
            _remoteEp  = remoteSipEp;
            _localEp   = localSipEp;

            // Set the base-class protected properties that back ListeningSIPEndPoint.
            // ListeningSIPEndPoint is computed as:
            //   new SIPEndPoint(SIPProtocol, ListeningIPAddress, Port, ID, null)
            // If these are not set, SIPTransport cannot key or route through this channel,
            // so it will try to open its own TLS/WSS connection (causing the SSL error).
            SIPProtocol        = localSipEp.Protocol;   // SIPProtocolsEnum.wss
            ListeningIPAddress = localSipEp.Address;
            Port               = localSipEp.Port;       // 0 = client-only, no fixed port
            IsReliable         = true;                  // WSS is TCP-based (reliable)
            IsSecure           = true;                  // WSS is TLS (secure)
        }

        // ── Connect ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Connects the WebSocket to the server URI. Must be called before adding
        /// this channel to a SIPTransport.
        /// </summary>
        public async Task ConnectAsync(CancellationToken ct = default)
        {
            _ws  = new ClientWebSocket();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _ws.Options.AddSubProtocol("sip");

            // Bypass TLS validation — necessary for self-signed certs on FreePBX
            _ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

            Logger.Info($"WssClientSipChannel connecting to {_serverUri}");
            await _ws.ConnectAsync(_serverUri, _cts.Token);
            Logger.Info($"WssClientSipChannel connected (State={_ws.State})");

            // Start receive loop in background
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token), CancellationToken.None);
        }

        // ── Receive loop ──────────────────────────────────────────────────────────

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buf = new byte[65536];
            var acc = new MemoryStream();

            try
            {
                while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Logger.Info("WssClientSipChannel: server sent WebSocket Close");
                        break;
                    }

                    acc.Write(buf, 0, result.Count);

                    if (result.EndOfMessage)
                    {
                        var data = acc.ToArray();
                        acc.SetLength(0);
                        acc.Position = 0;

                        if (SIPMessageReceived != null)
                        {
                            await SIPMessageReceived(
                                this,
                                _localEp,
                                _remoteEp,
                                data);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { /* normal shutdown */ }
            catch (Exception ex)
            {
                Logger.Warn(ex, "WssClientSipChannel receive loop error");
            }
        }

        // ── SIPChannel abstract implementations ──────────────────────────────────

        public override Task<SocketError> SendAsync(
            SIPEndPoint dstEndPoint,
            byte[] buffer,
            bool canInitiateConnection,
            string connectionIDHint)
        {
            return SendOverWsAsync(buffer);
        }

        public override Task<SocketError> SendSecureAsync(
            SIPEndPoint dstEndPoint,
            byte[] buffer,
            string serverCertificateName,
            bool canInitiateConnection,
            string connectionIDHint)
        {
            return SendOverWsAsync(buffer);
        }

        private async Task<SocketError> SendOverWsAsync(byte[] buffer)
        {
            if (_ws?.State != WebSocketState.Open)
            {
                Logger.Warn("WssClientSipChannel: attempted send on closed WebSocket");
                return SocketError.NotConnected;
            }

            try
            {
                await _ws.SendAsync(
                    new ArraySegment<byte>(buffer),
                    WebSocketMessageType.Text,
                    true,
                    _cts!.Token);
                return SocketError.Success;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "WssClientSipChannel send error");
                return SocketError.SocketError;
            }
        }

        public override bool HasConnection(string connectionID)
            => _ws?.State == WebSocketState.Open;

        public override bool HasConnection(SIPEndPoint remoteEndPoint)
            => _ws?.State == WebSocketState.Open;

        public override bool HasConnection(Uri serverUri)
            => _ws?.State == WebSocketState.Open;

        public override bool IsAddressFamilySupported(AddressFamily addressFamily)
            => addressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6;

        public override bool IsProtocolSupported(SIPProtocolsEnum protocol)
            => protocol is SIPProtocolsEnum.wss or SIPProtocolsEnum.ws;

        public override void Close()
        {
            _cts?.Cancel();
            try
            {
                if (_ws?.State == WebSocketState.Open)
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing",
                                   CancellationToken.None).GetAwaiter().GetResult();
            }
            catch { /* ignore errors during close */ }
            _ws?.Dispose();
            _ws = null;
        }

        public override void Dispose()
        {
            Close();
            GC.SuppressFinalize(this);
        }
    }
}
