# VOIPAT Phone — WebRTC SIP Phone Dialer

A production-ready **C# WPF SIP phone** built on [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) and NAudio. Supports WSS (WebSocket Secure) signaling, G.711 audio, DTMF, hold/resume, incoming calls, real-time network quality monitoring, and a full **SDK** for embedding phone control in your own apps.

---

## Features

| Category | Details |
|---|---|
| **Protocol** | SIP over WSS (WebSocket Secure), RFC 3261 |
| **Audio** | G.711 µ-law / A-law (PCMU/PCMA), NAudio Windows audio |
| **Call control** | Outgoing, incoming, hold/resume, mute, reject, DTMF (RFC 2833) |
| **Network quality** | Live packet loss %, jitter (RFC 3550), bitrate, quality tier (Excellent → Poor) |
| **UI** | Dark-themed WPF, system tray, signal-bar quality indicator, debug log |
| **Settings** | JSON persistence (`%AppData%\WebRtcPhoneDialer\settings.json`) |
| **SDK** | `PhoneDialerHost` reusable DLL + named-pipe IPC for remote control |
| **Example app** | WinForms SDK demo that connects to the running phone via IPC |

---

## System Requirements

- **OS:** Windows 10 / 11
- **Runtime:** .NET Framework 4.8.1
- **Build SDK:** .NET 8 SDK (used to build net481 targets)
- **Audio:** Windows audio device (microphone + speakers/headset)

---

## Solution Structure

```
WebRtcPhoneDialer.Core/          ← Reusable class library (net481)
  Interfaces/
    IPhoneService.cs             ← Primary API contract
    IAudioEndPointFactory.cs
    IAudioDeviceProvider.cs
  Models/
    AppSettings.cs               ← SIP credentials, audio, codec config
    CallSession.cs               ← Active call state
    WebRtcConfig.cs              ← Runtime WebRTC/SIP configuration
    NetworkQualityMetrics.cs     ← Live call quality snapshot
  Enums/
    CallState.cs                 ← Idle, Initiating, Ringing, Connected, OnHold, Ended, Failed
    RegistrationState.cs         ← Unregistered, Registering, Registered, Failed
  Events/
    CallStateChangedEventArgs.cs
    SipLogEventArgs.cs
    RtpLogEventArgs.cs
  Services/
    WebRtcService.cs             ← SIP stack, RTP media, network quality engine
    WssClientSipChannel.cs       ← WSS transport for SIPSorcery
    CallHistoryService.cs
  Ipc/
    IpcMessage.cs                ← Named-pipe wire format (JSON)
    PhoneIpcServer.cs            ← Pipe server (runs inside the main app)
    PhoneIpcClient.cs            ← Pipe client (used by the SDK ExampleApp)
  Utilities/
    PhoneNumberValidator.cs

WebRtcPhoneDialer.Windows/       ← Windows platform implementations (net481)
  WindowsAudioEndPointFactory.cs ← NAudio microphone + speaker endpoint
  WindowsAudioDeviceProvider.cs  ← Enumerates Windows audio devices
  PhoneDialerHost.cs             ← Convenience host — single entry point for parent apps

WebRtcPhoneDialer/               ← WPF desktop application (net481)
  Views/
    MainWindow.xaml / .cs        ← Main phone UI + IPC server startup
    SettingsWindow.xaml / .cs    ← SIP / audio / codec settings
    DebugWindow.xaml / .cs       ← SIP & RTP debug log viewer
  ViewModels/
    MainWindowViewModel.cs
    SettingsViewModel.cs
    BaseViewModel.cs
  App.xaml / App.xaml.cs

SDK/
  lib/                           ← Pre-built DLLs for distribution
  ExampleApp/                    ← WinForms demo — connects via IPC
    MainForm.cs                  ← Full remote-control UI
    Program.cs
  README.txt                     ← SDK quick-start guide
  ExampleApp.zip                 ← Distributable SDK bundle
```

---

## Build & Run

### Prerequisites

```bash
# Install .NET 8 SDK (if not already present)
winget install Microsoft.DotNet.SDK.8
```

### Build

```bash
dotnet restore
dotnet build WebRtcPhoneDialer.csproj
```

### Run

```bash
dotnet run --project WebRtcPhoneDialer.csproj
```

Executable is at: `bin\Debug\net481\WebRtcPhoneDialer.exe`

---

## Configuration

Settings are stored at `%AppData%\WebRtcPhoneDialer\settings.json` and edited via the in-app Settings window.

| Setting | Description |
|---|---|
| `SignalingServerUrl` | WSS server URL, e.g. `wss://sip.example.com:8089/ws` |
| `SipDomain` | SIP domain / registrar |
| `Username` | SIP account username |
| `Password` | SIP account password |
| `StunServer` | STUN server URI, e.g. `stun:stun.l.google.com:19302` |
| `InputDeviceId` | Microphone index (empty = system default) |
| `OutputDeviceId` | Speaker index (empty = system default) |
| `PreferredCodec` | `PCMU` (G.711 µ-law) or `PCMA` (G.711 A-law) |

---

## Network Quality Monitoring

During a connected call the engine samples RTP every **5 seconds** and fires `NetworkQualityChanged` with a `NetworkQualityMetrics` snapshot:

| Metric | Description |
|---|---|
| `PacketLossPct` | Estimated packet loss % (sequence-number gap detection) |
| `JitterMs` | RFC 3550 inter-arrival jitter in milliseconds |
| `RxKbps / TxKbps` | Receive / transmit bitrate (kbps) |
| `RxPps / TxPps` | Packets per second |
| `Codec` | Negotiated codec name (`PCMU`, `PCMA`) |
| `Quality` | `Excellent` · `Good` · `Fair` · `Poor` · `NoMedia` |

**Quality tiers:**

| Tier | Condition |
|---|---|
| Excellent | Loss ≤ 1% **and** Jitter ≤ 20 ms |
| Good | Loss ≤ 3% **and** Jitter ≤ 50 ms |
| Fair | Loss ≤ 8% **and** Jitter ≤ 100 ms |
| Poor | Loss > 8% **or** Jitter > 100 ms |
| No Media | No RTP packets received |

The WPF UI shows a **4-bar signal strength indicator** that updates in real time.

---

## SDK — Embed in Your App

The SDK ships as pre-built DLLs in `SDK/lib/`. Reference them in your project and use `PhoneDialerHost` as the single entry point.

### Standalone mode (your app owns the SIP stack)

```csharp
var host = new PhoneDialerHost();

// Wire events
host.RegistrationStateChanged += (s, state) => { /* Registered, Failed, … */ };
host.CallStateDetailChanged   += (s, e)     => { /* Ringing, Connected, Ended, … */ };
host.IncomingCall              += (s, call)  => { /* show incoming call UI */ };
host.IncomingCallCanceled      += (s, e)     => { /* caller hung up while ringing */ };
host.MicLevelChanged           += (s, level) => { /* 0.0–1.0 */ };
host.SpeakerLevelChanged       += (s, level) => { /* 0.0–1.0 */ };
host.NetworkQualityChanged     += (s, m)     => { /* NetworkQualityMetrics */ };
host.SipMessageLogged          += (s, e)     => { /* SIP debug traffic */ };
host.RtpDebugLogged            += (s, e)     => { /* RTP debug info */ };

// Configure and register
host.LoadAndApplySettings();          // loads %AppData%\WebRtcPhoneDialer\settings.json
await host.RegisterAsync();

// Call control
await host.InitiateCallAsync("100");
host.HoldCall();
host.UnholdCall();
host.SendDtmf(1);                     // tone 0–11 (0-9, *, #)
host.MuteMicrophone();
host.UnmuteMicrophone();
await host.EndCallAsync();

// Incoming calls
host.IncomingCall += async (s, call) => {
    await host.AnswerCallAsync();     // or host.RejectCall();
};

// Audio devices
var inputs  = host.AudioDevices.GetInputDevices();
var outputs = host.AudioDevices.GetOutputDevices();

// Diagnostics
var (sent, recv, bytesSent, bytesRecv) = host.GetRtpStats();
var duration = host.GetCallDuration();

// Cleanup
host.Unregister();
host.Dispose();
```

### IPC mode — control the running VOIPAT Phone app

If VOIPAT Phone is already running, use `PhoneIpcClient` to control it remotely from your own process — no duplicate SIP stack, no port conflicts.

```csharp
var phone = new PhoneIpcClient();

// Connection status
phone.ConnectionChanged += (s, connected) => {
    Console.WriteLine(connected ? "Connected to VOIPAT Phone" : "Disconnected");
};

// Same events as IPhoneService
phone.RegistrationStateChanged += (s, state) => { };
phone.CallStateDetailChanged   += (s, e)     => { };
phone.IncomingCall              += (s, call)  => { };
phone.NetworkQualityChanged     += (s, m)     => { };
phone.MicLevelChanged           += (s, level) => { };
phone.SipMessageLogged          += (s, e)     => { };

// Start the auto-reconnect loop
phone.Connect();

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
phone.ShowWindow();   // bring the VOIPAT Phone window to front

phone.Dispose();
```

The IPC transport is a **Windows named pipe** (`\\.\pipe\WebRtcPhoneDialer_IPC`) with line-delimited JSON messages. VOIPAT Phone starts the pipe server automatically on launch.

---

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| SIPSorcery | 8.0.23 | SIP stack (REGISTER, INVITE, BYE, …) |
| NAudio | 1.10.0 | Windows audio capture/playback |
| Newtonsoft.Json | 13.0.3 | JSON settings & IPC serialization |
| NLog | 5.2.7 | Logging |
| RestSharp | 107.3.0 | HTTP utilities |
| Microsoft.Extensions.Logging | 9.0.0 | SIPSorcery logging bridge |

---

## Troubleshooting

| Problem | Fix |
|---|---|
| `System.Memory` assembly mismatch (HRESULT 0x80131040) | Ensure `App.config` has all binding redirects. `<supportedRuntime>` must be in `<startup>`, not inside `<runtime>`. |
| Registration fails immediately | Verify `SignalingServerUrl` starts with `wss://` and the SIP server is reachable |
| No audio | Check microphone/speaker device IDs in settings. Leave blank for system default. |
| ExampleApp shows "Not connected" | Start VOIPAT Phone first — it must be running to host the IPC pipe server |
| Build error: file locked | Kill the running `WebRtcPhoneDialer.exe` before rebuilding: `taskkill //F //IM WebRtcPhoneDialer.exe` |

---

## Roadmap

- [ ] SRTP / DTLS media encryption
- [ ] Opus codec support
- [ ] Video call support
- [ ] Call transfer (REFER)
- [ ] Conference calling
- [ ] Contact book integration
- [ ] Android / iOS port (see companion repos)

---

## License

MIT — free for commercial and personal use.
