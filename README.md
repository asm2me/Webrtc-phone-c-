# WebRTC Phone Dialer

A modern C# WPF application for making VoIP calls using WebRTC technology. This application provides a user-friendly interface for initiating and managing phone calls over the internet.

## Features

- **WebRTC-based VoIP**: Make calls using WebRTC peer-to-peer technology
- **Dial Pad Interface**: Classic telephone-style dial pad for easy number entry
- **Phone Number/SIP URI Support**: Supports both standard phone numbers and SIP addresses
- **Call Management**: Initiate, monitor, and terminate calls
- **Configuration**: Customize STUN and TURN servers for NAT traversal
- **Call Duration Tracking**: Monitor call duration in real-time
- **Call History**: Track previous calls
- **Logging**: Built-in logging with NLog for debugging and troubleshooting

## System Requirements

- Windows 10 or later
- .NET 6.0 or higher
- Visual Studio 2022 (for development)

## Installation

1. Clone the repository:
   ```bash
   git clone <repository-url>
   cd webrtc-phone-dialer
   ```

2. Open the solution in Visual Studio 2022

3. Restore NuGet packages:
   ```bash
   dotnet restore
   ```

4. Build the project:
   ```bash
   dotnet build
   ```

## Building the Project

### Using Visual Studio
1. Open `WebRtcPhoneDialer.csproj` in Visual Studio
2. Click Build → Build Solution (Ctrl+Shift+B)

### Using Command Line
```bash
dotnet build
dotnet build --configuration Release
```

## Running the Application

### From Visual Studio
1. Press F5 or click Debug → Start Debugging

### From Command Line
```bash
dotnet run
```

### Using Compiled Executable
After building, the executable will be located in:
- Debug: `bin\Debug\net6.0-windows\WebRtcPhoneDialer.exe`
- Release: `bin\Release\net6.0-windows\WebRtcPhoneDialer.exe`

## Usage

### Making a Call

1. **Enter Phone Number/SIP URI**: Type the destination phone number or SIP address into the input field
   - Phone numbers: Enter digits (e.g., 1234567890)
   - SIP URIs: Enter in format user@domain (e.g., john@sip.example.com)

2. **Use Dial Pad**: Click numbers on the dial pad to enter the phone number

3. **Initiate Call**: Click the "Call" button to initiate the call

4. **Monitor Call**: The call status and duration are displayed in real-time

5. **End Call**: Click the "Hang Up" button to end the active call

### Configuring Servers

1. Click the "Settings" expander at the bottom of the window
2. Enter STUN server address (e.g., `stun:stun.l.google.com:19302`)
3. Enter ICE server(s) - one per line
4. Click "Save Settings"

Default STUN/ICE servers are provided:
- `stun:stun.l.google.com:19302`
- `stun:stun1.l.google.com:19302`

## Project Structure

```
WebRtcPhoneDialer/
├── Views/
│   └── MainWindow.xaml           # Main UI
│   └── MainWindow.xaml.cs        # UI logic
├── ViewModels/
│   ├── MainWindowViewModel.cs    # Main view model
│   └── BaseViewModel.cs          # Base view model with INotifyPropertyChanged
├── Models/
│   ├── CallSession.cs            # Call session model
│   └── WebRtcConfig.cs           # WebRTC configuration model
├── Services/
│   └── WebRtcService.cs          # Core WebRTC service
├── Utilities/
│   └── PhoneNumberValidator.cs   # Phone number validation
├── App.xaml                       # Application resources
├── App.xaml.cs                    # Application code-behind
└── WebRtcPhoneDialer.csproj      # Project file
```

## Dependencies

- **NLog**: Logging framework
- **RestSharp**: HTTP client library
- **Newtonsoft.Json**: JSON serialization
- **google-api-dotnet-client**: Google APIs support

## WebRTC Implementation Notes

This project provides a foundation for WebRTC integration. To add actual WebRTC functionality:

1. **Add WebRTC Library**: Consider adding a WebRTC library such as:
   - WebRtcOrgNETAPI (Mozilla WebRTC)
   - Pion WebRTC (Go-based, requires interop)
   - Or integrate with a SIP/WebRTC gateway

2. **Implement Peer Connection**:
   - Modify `CreatePeerConnection()` in `WebRtcService.cs`
   - Set up ICE candidates
   - Handle offer/answer signaling

3. **Audio Handling**:
   - Integrate audio capture from microphone
   - Handle audio playback from speakers
   - Implement audio codec selection

4. **Signaling Protocol**:
   - Implement SIP (Session Initiation Protocol)
   - Or use WebSocket-based signaling
   - Handle SDP (Session Description Protocol)

## Configuration Files

### NLog Configuration

Create a `nlog.config` file in the application root for logging configuration:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd"
      xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <targets>
    <target name="file" xsi:type="File" fileName="logs/app-${shortdate}.log" />
    <target name="console" xsi:type="Console" />
  </targets>
  <rules>
    <logger name="*" minlevel="Info" writeTo="console,file" />
  </rules>
</nlog>
```

## Troubleshooting

### Application Won't Start
- Ensure .NET 6.0 or higher is installed
- Check that all NuGet packages are restored
- Verify Windows 10 or later is installed

### No Sound During Calls
- Check system audio settings
- Ensure microphone and speaker permissions are granted
- Verify audio codecs are properly configured

### Cannot Connect to Peer
- Test STUN/TURN server connectivity
- Check firewall settings
- Verify ICE server configuration
- Check network connectivity

## Logging

Logs are output to the console by default. Configure logging in `WebRtcService.cs` initialization or with an `nlog.config` file.

## Future Enhancements

- [ ] Video call support
- [ ] Call recording
- [ ] Contact management
- [ ] Call transfer
- [ ] Conference calling
- [ ] Screen sharing
- [ ] Instant messaging
- [ ] Mobile app support
- [ ] Cloud-based call history
- [ ] Advanced security features (SRTP, DTLS)

## License

This project is provided as-is for educational and development purposes.

## Contributing

Contributions are welcome! Please fork the repository and submit pull requests with improvements.

## Support

For issues, questions, or suggestions, please open an issue in the repository.

---

**Note**: This is a foundational WebRTC phone dialer. For production use, additional security measures, error handling, and real WebRTC library integration are recommended.
