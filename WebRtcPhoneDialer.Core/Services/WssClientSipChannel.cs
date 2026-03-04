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

namespace WebRtcPhoneDialer.Core.Services
{
    /// <summary>
    /// A client-side WebSocket SIP channel that pre-connects to a full WSS URI
    /// (including path, e.g. /ws) and bypasses TLS certificate validation for
    /// self-signed certificates on FreePBX/Asterisk servers.
    /// </summary>
    internal sealed class WssClientSipChannel : SIPChannel
    {
        private readonly Uri         _serverUri;
        private readonly SIPEndPoint _remoteEp;
        private readonly SIPEndPoint _localEp;
        private ClientWebSocket?     _ws;
        private CancellationTokenSource? _cts;

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public WssClientSipChannel(Uri serverUri, SIPEndPoint localSipEp, SIPEndPoint remoteSipEp)
            : base(Encoding.UTF8, Encoding.UTF8)
        {
            _serverUri = serverUri;
            _remoteEp  = remoteSipEp;
            _localEp   = localSipEp;

            SIPProtocol        = localSipEp.Protocol;
            ListeningIPAddress = localSipEp.Address;
            Port               = localSipEp.Port;
            IsReliable         = true;
            IsSecure           = true;
        }

        public async Task ConnectAsync(CancellationToken ct = default)
        {
            _ws  = new ClientWebSocket();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _ws.Options.AddSubProtocol("sip");
            _ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

            Logger.Info($"WssClientSipChannel connecting to {_serverUri}");
            await _ws.ConnectAsync(_serverUri, _cts.Token);
            Logger.Info($"WssClientSipChannel connected (State={_ws.State})");

            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token), CancellationToken.None);
        }

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
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.Warn(ex, "WssClientSipChannel receive loop error");
            }
        }

        public override Task<SocketError> SendAsync(
            SIPEndPoint dstEndPoint, byte[] buffer, bool canInitiateConnection, string connectionIDHint)
            => SendOverWsAsync(buffer);

        public override Task<SocketError> SendSecureAsync(
            SIPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName,
            bool canInitiateConnection, string connectionIDHint)
            => SendOverWsAsync(buffer);

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
            catch { }
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
