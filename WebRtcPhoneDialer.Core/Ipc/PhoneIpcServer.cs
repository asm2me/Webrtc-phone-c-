using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WebRtcPhoneDialer.Core.Interfaces;

namespace WebRtcPhoneDialer.Core.Ipc
{
    /// <summary>
    /// Named-pipe server that wraps an IPhoneService and exposes it to external processes.
    /// Start automatically after creating: it listens for one client at a time.
    /// </summary>
    public sealed class PhoneIpcServer : IDisposable
    {
        public const string PipeName = "WebRtcPhoneDialer_IPC";

        private readonly IPhoneService _svc;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private StreamWriter? _writer;
        private readonly object _wLock = new object();
        private bool _disposed;

        /// <summary>Fired when the remote client requests the main window to be shown.</summary>
        public event EventHandler? ShowWindowRequested;

        public PhoneIpcServer(IPhoneService svc)
        {
            _svc = svc;
            SubscribeEvents();
            Task.Run(AcceptLoop);
        }

        // ── Accept loop ────────────────────────────────────────────────────────

        private async Task AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    await pipe.WaitForConnectionAsync(_cts.Token);

                    lock (_wLock) { _writer = new StreamWriter(pipe) { AutoFlush = true }; }

                    // Push current state to the newly connected client
                    Send("RegState", new Dictionary<string, string?> {
                        ["s"] = _svc.RegistrationState.ToString(),
                        ["m"] = _svc.RegistrationMessage
                    });

                    if (_svc.HasActiveCall)
                    {
                        var call = _svc.GetCurrentCall();
                        if (call != null)
                            Send("CallState", new Dictionary<string, string?> {
                                ["new"]    = "Connected",
                                ["prev"]   = "Connected",
                                ["remote"] = call.RemoteParty,
                                ["reason"] = null,
                                ["fail"]   = null
                            });
                    }

                    // Read commands
                    var reader = new StreamReader(pipe);
                    string? line;
                    while (!_cts.IsCancellationRequested && (line = await reader.ReadLineAsync()) != null)
                    {
                        try
                        {
                            var msg = JsonConvert.DeserializeObject<IpcMessage>(line);
                            if (msg != null) Dispatch(msg);
                        }
                        catch { /* bad JSON — ignore */ }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { /* client disconnected — loop to accept next */ }
                finally
                {
                    lock (_wLock) { _writer = null; }
                    pipe?.Dispose();
                }
            }
        }

        // ── Command dispatch ────────────────────────────────────────────────────

        private void Dispatch(IpcMessage msg)
        {
            if (msg.Kind != "cmd") return;
            switch (msg.Name)
            {
                case "Register":   _ = _svc.RegisterAsync();   break;
                case "Unregister": _svc.Unregister();          break;
                case "HangUp":     _ = _svc.EndCallAsync();    break;
                case "Hold":       _svc.HoldCall();            break;
                case "Unhold":     _svc.UnholdCall();          break;
                case "Answer":     _ = _svc.AnswerCallAsync(); break;
                case "Reject":     _svc.RejectCall();          break;
                case "Mute":       _svc.MuteMicrophone();      break;
                case "Unmute":     _svc.UnmuteMicrophone();    break;

                case "Call":
                    if (msg.Data.TryGetValue("n", out var num) && num != null)
                        _ = _svc.InitiateCallAsync(num);
                    break;

                case "Dtmf":
                    if (msg.Data.TryGetValue("t", out var t) && byte.TryParse(t, out var tone))
                        _svc.SendDtmf(tone);
                    break;

                case "ShowWindow":
                    ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        // ── Event forwarding ────────────────────────────────────────────────────

        private void SubscribeEvents()
        {
            _svc.RegistrationStateChanged += (_, e) =>
                Send("RegState", new Dictionary<string, string?> {
                    ["s"] = e.ToString(),
                    ["m"] = _svc.RegistrationMessage
                });

            _svc.CallStateDetailChanged += (_, e) =>
                Send("CallState", new Dictionary<string, string?> {
                    ["new"]    = e.NewState.ToString(),
                    ["prev"]   = e.PreviousState.ToString(),
                    ["remote"] = e.Call.RemoteParty,
                    ["reason"] = e.Reason,
                    ["fail"]   = _svc.LastCallFailureReason
                });

            _svc.IncomingCall += (_, e) =>
                Send("Incoming", new Dictionary<string, string?> { ["remote"] = e.RemoteParty });

            _svc.IncomingCallCanceled += (_, e) =>
                Send("IncomingCanceled", new Dictionary<string, string?>());

            _svc.MicLevelChanged += (_, e) =>
                Send("MicLevel", new Dictionary<string, string?> { ["v"] = e.ToString("F3") });

            _svc.SpeakerLevelChanged += (_, e) =>
                Send("SpkLevel", new Dictionary<string, string?> { ["v"] = e.ToString("F3") });

            _svc.NetworkQualityChanged += (_, e) =>
                Send("NetQuality", new Dictionary<string, string?> {
                    ["q"]     = e.Quality.ToString(),
                    ["loss"]  = e.PacketLossPct.ToString("F1"),
                    ["jitr"]  = e.JitterMs.ToString("F1"),
                    ["rxk"]   = e.RxKbps.ToString(),
                    ["txk"]   = e.TxKbps.ToString(),
                    ["rxp"]   = e.RxPps.ToString(),
                    ["txp"]   = e.TxPps.ToString(),
                    ["codec"] = e.Codec,
                    ["media"] = e.HasMedia.ToString()
                });

            _svc.SipMessageLogged += (_, e) =>
                Send("SipLog", new Dictionary<string, string?> {
                    ["dir"] = e.Direction,
                    ["msg"] = e.Message
                });

            _svc.RtpDebugLogged += (_, e) =>
                Send("RtpLog", new Dictionary<string, string?> { ["msg"] = e.Message });
        }

        private void Send(string name, Dictionary<string, string?> data)
        {
            lock (_wLock)
            {
                if (_writer == null) return;
                try
                {
                    _writer.WriteLine(JsonConvert.SerializeObject(
                        new IpcMessage { Kind = "evt", Name = name, Data = data }));
                }
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
