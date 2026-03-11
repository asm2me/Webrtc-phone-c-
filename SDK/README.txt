VOIPAT Phone — SDK
==================
Target Framework : .NET Framework 4.8.1 (net481)
Windows only     : requires NAudio / Windows audio APIs


STRUCTURE
---------
  lib/                        All required DLLs — add as references in your project
  ExampleApp/                 Complete WinForms example (IPC remote-control demo)
  ExampleApp.zip              Distributable SDK bundle
  README.txt                  This file


REQUIRED DLLs (from lib/)
--------------------------
  WebRtcPhoneDialer.Core.dll      Core engine — SIP stack, RTP, network quality, IPC
  WebRtcPhoneDialer.Windows.dll   Windows audio (NAudio microphone + speaker)
  + all dependency DLLs in lib/


TWO USAGE MODES
---------------

  MODE 1 — Standalone (your app owns the SIP stack)
  --------------------------------------------------
  Use PhoneDialerHost as the single entry point.
  Your app registers with the SIP server directly.

    var host = new PhoneDialerHost();
    host.LoadAndApplySettings();          // reads %AppData%\WebRtcPhoneDialer\settings.json

    // Wire events
    host.RegistrationStateChanged += (s, state) => { };
    host.CallStateDetailChanged   += (s, e)     => { };
    host.IncomingCall              += (s, call)  => { };
    host.IncomingCallCanceled      += (s, e)     => { };
    host.MicLevelChanged           += (s, level) => { };   // 0.0–1.0, ~20 fps
    host.SpeakerLevelChanged       += (s, level) => { };   // 0.0–1.0, ~20 fps
    host.NetworkQualityChanged     += (s, m)     => { };   // every 5 s during a call
    host.SipMessageLogged          += (s, e)     => { };
    host.RtpDebugLogged            += (s, e)     => { };

    await host.RegisterAsync();
    await host.InitiateCallAsync("100");
    host.HoldCall();
    host.UnholdCall();
    host.SendDtmf(1);                     // 0-9 = digits, 10 = *, 11 = #
    host.MuteMicrophone();
    host.UnmuteMicrophone();
    await host.EndCallAsync();

    host.IncomingCall += async (s, call) => {
        await host.AnswerCallAsync();     // or host.RejectCall();
    };

    // Audio device enumeration
    var inputs  = host.AudioDevices.GetInputDevices();
    var outputs = host.AudioDevices.GetOutputDevices();

    // Diagnostics
    var (sent, recv, bytesSent, bytesRecv) = host.GetRtpStats();
    var duration = host.GetCallDuration();

    host.Unregister();
    host.Dispose();


  MODE 2 — IPC Remote Control (control the running VOIPAT Phone app)
  -------------------------------------------------------------------
  Use PhoneIpcClient to control an already-running VOIPAT Phone instance
  over a named pipe. No duplicate SIP stack, no port conflicts.

  VOIPAT Phone starts the pipe server automatically on launch.
  Pipe name: \\.\pipe\WebRtcPhoneDialer_IPC

    var phone = new PhoneIpcClient();

    // Connection status
    phone.ConnectionChanged += (s, connected) => {
        Console.WriteLine(connected ? "Connected" : "Disconnected — retrying…");
    };

    // Same events as standalone mode
    phone.RegistrationStateChanged += (s, state) => { };
    phone.CallStateDetailChanged   += (s, e)     => { };
    phone.IncomingCall              += (s, call)  => { };
    phone.IncomingCallCanceled      += (s, e)     => { };
    phone.MicLevelChanged           += (s, level) => { };
    phone.SpeakerLevelChanged       += (s, level) => { };
    phone.NetworkQualityChanged     += (s, m)     => { };
    phone.SipMessageLogged          += (s, e)     => { };
    phone.RtpDebugLogged            += (s, e)     => { };

    phone.Connect();        // starts background auto-reconnect loop

    // Commands (fire-and-forget; result arrives via events)
    phone.Register();
    phone.Call("100");
    phone.Hold();
    phone.Unhold();
    phone.Mute();
    phone.Unmute();
    phone.SendDtmf(1);
    phone.Answer();
    phone.Reject();
    phone.HangUp();
    phone.ShowWindow();     // bring the VOIPAT Phone window to front

    phone.Dispose();


NETWORK QUALITY EVENT (every 5 seconds during a call)
------------------------------------------------------
  host.NetworkQualityChanged += (s, m) => {
      // m.Quality       — Excellent | Good | Fair | Poor | NoMedia
      // m.PacketLossPct — estimated packet loss %
      // m.JitterMs      — RFC 3550 inter-arrival jitter in milliseconds
      // m.RxKbps        — receive bitrate (kbps)
      // m.TxKbps        — transmit bitrate (kbps)
      // m.RxPps         — receive packets per second
      // m.TxPps         — transmit packets per second
      // m.Codec         — "PCMU" or "PCMA"
      // m.HasMedia      — true once the first RTP packet is received
  };

  Quality tiers:
    Excellent — loss <= 1%  and jitter <= 20 ms
    Good      — loss <= 3%  and jitter <= 50 ms
    Fair      — loss <= 8%  and jitter <= 100 ms
    Poor      — loss  > 8%  or  jitter >  100 ms
    No Media  — no RTP packets received yet


EXAMPLE APP
-----------
  ExampleApp/ is a WinForms app demonstrating MODE 2 (IPC remote control).

  To run:
    1. Start VOIPAT Phone (WebRtcPhoneDialer.exe) first.
    2. Run ExampleApp.exe — it connects automatically.
    3. The "Connection" panel turns green and shows "Connected to VOIPAT Phone".
    4. Use Register, Call, Hold, Mute, DTMF, etc. to control the phone remotely.
    5. Click "Show Phone" to bring the VOIPAT Phone window to the front.

  To build:
    cd SDK\ExampleApp
    dotnet build ExampleApp.csproj


SETTINGS FILE
-------------
  Location: %AppData%\WebRtcPhoneDialer\settings.json

  {
    "SignalingServerUrl": "wss://your-sip-server:8089/ws",
    "SipDomain":         "your-sip-domain",
    "Username":          "1001",
    "Password":          "secret",
    "StunServer":        "stun:stun.l.google.com:19302",
    "InputDeviceId":     "",    // empty = system default
    "OutputDeviceId":    "",    // empty = system default
    "PreferredCodec":    "PCMU" // or "PCMA"
  }
