using System;
using System.Text;
using System.Windows;
using WebRtcPhoneDialer.Services;

namespace WebRtcPhoneDialer.Views
{
    public partial class DebugWindow : Window
    {
        private readonly WebRtcService _webRtcService;

        public DebugWindow(WebRtcService webRtcService)
        {
            InitializeComponent();
            _webRtcService = webRtcService;
            _webRtcService.RegistrationStateChanged += OnStateChanged;
            RefreshInfo();
        }

        private void OnStateChanged(object? sender, RegistrationState state)
        {
            Dispatcher.Invoke(RefreshInfo);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshInfo();

        private void RefreshInfo()
        {
            var config = _webRtcService.GetConfiguration();
            var call = _webRtcService.GetCurrentCall();

            var sb = new StringBuilder();
            sb.AppendLine("=== Registration ===");
            sb.AppendLine($"State:   {_webRtcService.RegistrationState}");
            sb.AppendLine($"Message: {_webRtcService.RegistrationMessage}");
            sb.AppendLine();
            sb.AppendLine("=== WebRTC Configuration ===");
            sb.AppendLine($"Username:      {config.Username ?? "(not set)"}");
            sb.AppendLine($"Signaling URL: {config.SignalingServerUrl ?? "(not set)"}");
            sb.AppendLine($"STUN Server:   {config.StunServer ?? "(not set)"}");
            sb.AppendLine($"TURN Server:   {config.TurnServer ?? "(not set)"}");
            sb.AppendLine($"TURN Username: {config.TurnUsername ?? "(not set)"}");
            sb.AppendLine($"ICE Servers:   {config.IceServers?.Count ?? 0}");
            sb.AppendLine();
            sb.AppendLine("=== Audio/Codec ===");
            sb.AppendLine($"Audio Enabled:     {config.EnableAudio}");
            sb.AppendLine($"Audio Codec:       {config.AudioCodecName ?? "(not set)"}");
            sb.AppendLine($"Echo Cancellation: {config.EchoCancellation}");
            sb.AppendLine($"Noise Suppression: {config.NoiseSuppression}");
            sb.AppendLine($"Input Volume:      {config.InputVolume}");
            sb.AppendLine($"Output Volume:     {config.OutputVolume}");
            sb.AppendLine($"Video Enabled:     {config.EnableVideo}");
            sb.AppendLine($"Video Codec:       {config.VideoCodecName ?? "(not set)"}");
            sb.AppendLine();
            sb.AppendLine("=== Active Call ===");
            if (call == null)
            {
                sb.AppendLine("No active call");
            }
            else
            {
                sb.AppendLine($"Remote:   {call.RemoteParty}");
                sb.AppendLine($"State:    {call.State}");
                sb.AppendLine($"Duration: {_webRtcService.GetCallDuration()}");
            }

            DebugText.Text = sb.ToString();
        }

        protected override void OnClosed(EventArgs e)
        {
            _webRtcService.RegistrationStateChanged -= OnStateChanged;
            base.OnClosed(e);
        }
    }
}
