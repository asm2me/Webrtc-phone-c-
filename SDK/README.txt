WebRTC Phone Dialer SDK
=======================

Structure:
  lib/          - All required DLLs (reference these in your project)
  ExampleApp/   - Complete WinForms example demonstrating all features

Required DLLs (add as references):
  WebRtcPhoneDialer.Core.dll      - Core engine (cross-platform)
  WebRtcPhoneDialer.Windows.dll   - Windows audio implementation
  + all dependency DLLs in lib/

Quick Start:
  var host = new PhoneDialerHost();
  host.LoadAndApplySettings();

  // Wire events
  host.RegistrationStateChanged += (s, state) => { };
  host.CallStateDetailChanged   += (s, e) => { };
  host.IncomingCall              += (s, call) => { };
  host.MicLevelChanged           += (s, level) => { };
  host.SpeakerLevelChanged       += (s, level) => { };
  host.SipMessageLogged          += (s, e) => { };
  host.RtpDebugLogged            += (s, e) => { };

  // Register
  await host.RegisterAsync();

  // Call
  await host.InitiateCallAsync("100");
  host.HoldCall();
  host.UnholdCall();
  host.SendDtmf(1);
  host.MuteMicrophone();
  host.UnmuteMicrophone();
  await host.EndCallAsync();

  // Incoming calls
  host.IncomingCall += async (s, call) => {
      await host.AnswerCallAsync();   // or host.RejectCall();
  };

  // Settings
  host.SaveAndApplySettings(settings);
  var devices = host.AudioDevices.GetInputDevices();

  // Cleanup
  host.Unregister();
  host.Dispose();

Target Framework: .NET Framework 4.8.1 (net481)
