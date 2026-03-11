using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WebRtcPhoneDialer.Core.Enums;
using WebRtcPhoneDialer.Core.Events;
using WebRtcPhoneDialer.Core.Models;

namespace WebRtcPhoneDialer.Core.Ipc
{
    /// <summary>
    /// Named-pipe client that connects to PhoneIpcServer running inside the main phone app.
    /// Exposes the same events and command methods as IPhoneService, so it can be used
    /// as a drop-in controller in external WinForms / WPF apps.
    /// Reconnects automatically when the server restarts.
    /// </summary>
    public sealed class PhoneIpcClient : IDisposable
    {
        // ── Events ─────────────────────────────────────────────────────────────
        public event EventHandler<RegistrationState>?         RegistrationStateChanged;
        public event EventHandler<CallStateChangedEventArgs>? CallStateDetailChanged;
        public event EventHandler<CallSession>?               IncomingCall;
        public event EventHandler?                            IncomingCallCanceled;
        public event EventHandler<float>?                     MicLevelChanged;
        public event EventHandler<float>?                     SpeakerLevelChanged;
        public event EventHandler<NetworkQualityMetrics>?     NetworkQualityChanged;
        public event EventHandler<SipLogEventArgs>?           SipMessageLogged;
        public event EventHandler<RtpLogEventArgs>?           RtpDebugLogged;

        /// <summary>Fired when the pipe connection is established (true) or lost (false).</summary>
        public event EventHandler<bool>? ConnectionChanged;

        // ── State ──────────────────────────────────────────────────────────────
        public RegistrationState RegistrationState { get; private set; } = RegistrationState.Unregistered;
        public string RegistrationMessage { get; private set; } = "";
        public bool IsConnected { get; private set; }
        public bool HasActiveCall { get; private set; }
        public bool IsRegistered => RegistrationState == RegistrationState.Registered;
        public string? LastCallFailureReason { get; private set; }

        private CallSession? _currentCall;
        private DateTime     _callConnectedAt;

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private StreamWriter? _writer;
        private readonly object _wLock = new object();
        private bool _disposed;

        // ── Connection ─────────────────────────────────────────────────────────

        /// <summary>Start background connect-and-listen loop.</summary>
        public void Connect() => Task.Run(ConnectLoop);

        private async Task ConnectLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                NamedPipeClientStream? pipe = null;
                try
                {
                    pipe = new NamedPipeClientStream(".", PhoneIpcServer.PipeName,
                        PipeDirection.InOut, PipeOptions.Asynchronous);

                    // ConnectAsync(timeout) — .NET Framework doesn't have CancellationToken overload
                    await pipe.ConnectAsync(3000);

                    lock (_wLock) { _writer = new StreamWriter(pipe) { AutoFlush = true }; }
                    IsConnected = true;
                    ConnectionChanged?.Invoke(this, true);

                    var reader = new StreamReader(pipe);
                    string? line;
                    while (!_cts.IsCancellationRequested && (line = await reader.ReadLineAsync()) != null)
                    {
                        try
                        {
                            var msg = JsonConvert.DeserializeObject<IpcMessage>(line);
                            if (msg != null) ProcessEvent(msg);
                        }
                        catch { /* bad JSON — ignore */ }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { /* server not up yet — will retry */ }
                finally
                {
                    lock (_wLock) { _writer = null; }
                    pipe?.Dispose();
                    if (IsConnected)
                    {
                        IsConnected = false;
                        ConnectionChanged?.Invoke(this, false);
                    }
                }

                // Wait before retrying
                try { await Task.Delay(2000, _cts.Token); }
                catch (OperationCanceledException) { break; }
            }
        }

        // ── Event processing ────────────────────────────────────────────────────

        private void ProcessEvent(IpcMessage msg)
        {
            if (msg.Kind != "evt") return;

            switch (msg.Name)
            {
                case "RegState":
                    if (Enum.TryParse<RegistrationState>(Get(msg, "s"), out var rs))
                    {
                        RegistrationState   = rs;
                        RegistrationMessage = Get(msg, "m") ?? "";
                        RegistrationStateChanged?.Invoke(this, rs);
                    }
                    break;

                case "CallState":
                    Enum.TryParse<CallState>(Get(msg, "new"),  out var newSt);
                    Enum.TryParse<CallState>(Get(msg, "prev"), out var prevSt);
                    var remote = Get(msg, "remote") ?? "";
                    LastCallFailureReason = Get(msg, "fail");

                    switch (newSt)
                    {
                        case CallState.Connected:
                            _callConnectedAt = DateTime.UtcNow;
                            HasActiveCall    = true;
                            break;
                        case CallState.Ended:
                        case CallState.Failed:
                            HasActiveCall = false;
                            _currentCall  = null;
                            break;
                        default:
                            HasActiveCall = true;
                            break;
                    }

                    _currentCall = new CallSession { RemoteParty = remote };
                    CallStateDetailChanged?.Invoke(this,
                        new CallStateChangedEventArgs(prevSt, newSt, _currentCall, Get(msg, "reason")));
                    break;

                case "Incoming":
                    _currentCall  = new CallSession { RemoteParty = Get(msg, "remote") ?? "" };
                    HasActiveCall = true;
                    IncomingCall?.Invoke(this, _currentCall);
                    break;

                case "IncomingCanceled":
                    HasActiveCall = false;
                    _currentCall  = null;
                    IncomingCallCanceled?.Invoke(this, EventArgs.Empty);
                    break;

                case "MicLevel":
                    if (float.TryParse(Get(msg, "v"), out var mic))
                        MicLevelChanged?.Invoke(this, mic);
                    break;

                case "SpkLevel":
                    if (float.TryParse(Get(msg, "v"), out var spk))
                        SpeakerLevelChanged?.Invoke(this, spk);
                    break;

                case "NetQuality":
                    Enum.TryParse<NetworkCallQuality>(Get(msg, "q"), out var qual);
                    float.TryParse(Get(msg, "loss"), out var loss);
                    float.TryParse(Get(msg, "jitr"), out var jitr);
                    int.TryParse(Get(msg, "rxk"),   out var rxk);
                    int.TryParse(Get(msg, "txk"),   out var txk);
                    int.TryParse(Get(msg, "rxp"),   out var rxp);
                    int.TryParse(Get(msg, "txp"),   out var txp);
                    bool.TryParse(Get(msg, "media"), out var media);
                    NetworkQualityChanged?.Invoke(this, new NetworkQualityMetrics {
                        Quality       = qual,
                        PacketLossPct = loss,
                        JitterMs      = jitr,
                        RxKbps = rxk, TxKbps = txk,
                        RxPps  = rxp, TxPps  = txp,
                        HasMedia = media,
                        Codec    = Get(msg, "codec")
                    });
                    break;

                case "SipLog":
                    SipMessageLogged?.Invoke(this,
                        new SipLogEventArgs(Get(msg, "dir") ?? "", Get(msg, "msg") ?? ""));
                    break;

                case "RtpLog":
                    RtpDebugLogged?.Invoke(this, new RtpLogEventArgs(Get(msg, "msg") ?? ""));
                    break;
            }
        }

        // ── Commands ───────────────────────────────────────────────────────────

        public void ShowWindow() => Cmd("ShowWindow");
        public void Register()  => Cmd("Register");
        public void Unregister() => Cmd("Unregister");
        public void HangUp()   => Cmd("HangUp");
        public void Hold()     => Cmd("Hold");
        public void Unhold()   => Cmd("Unhold");
        public void Answer()   => Cmd("Answer");
        public void Reject()   => Cmd("Reject");
        public void Mute()     => Cmd("Mute");
        public void Unmute()   => Cmd("Unmute");

        public void Call(string number) => Send(new IpcMessage {
            Kind = "cmd", Name = "Call",
            Data = { ["n"] = number }
        });

        public void SendDtmf(byte tone) => Send(new IpcMessage {
            Kind = "cmd", Name = "Dtmf",
            Data = { ["t"] = tone.ToString() }
        });

        public CallSession? GetCurrentCall() => _currentCall;

        public TimeSpan GetCallDuration()
            => _currentCall != null && HasActiveCall
                ? DateTime.UtcNow - _callConnectedAt
                : TimeSpan.Zero;

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string? Get(IpcMessage msg, string key)
            => msg.Data.TryGetValue(key, out var v) ? v : null;

        private void Cmd(string name) => Send(new IpcMessage { Kind = "cmd", Name = name });

        private void Send(IpcMessage msg)
        {
            lock (_wLock)
            {
                if (_writer == null) return;
                try { _writer.WriteLine(JsonConvert.SerializeObject(msg)); }
                catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
        }
    }
}
