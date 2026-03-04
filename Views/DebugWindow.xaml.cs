using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using WebRtcPhoneDialer.Models;
using WebRtcPhoneDialer.Services;

namespace WebRtcPhoneDialer.Views
{
    public partial class DebugWindow : Window
    {
        private readonly WebRtcService _webRtcService;

        // Colour palette (dark-terminal theme)
        private static readonly SolidColorBrush BrushTimestamp = new(Color.FromRgb(0x80, 0x80, 0x80));
        private static readonly SolidColorBrush BrushSend      = new(Color.FromRgb(0x56, 0x9C, 0xD6)); // blue  — sent SIP
        private static readonly SolidColorBrush BrushRecv      = new(Color.FromRgb(0x6A, 0x99, 0x55)); // green — received SIP
        private static readonly SolidColorBrush BrushError     = new(Color.FromRgb(0xF4, 0x47, 0x47)); // red   — 4xx/5xx/6xx
        private static readonly SolidColorBrush BrushInfo      = new(Color.FromRgb(0xFF, 0xD7, 0x00)); // yellow — events
        private static readonly SolidColorBrush BrushBody      = new(Color.FromRgb(0xCC, 0xCC, 0xCC)); // light grey — headers/SDP
        private static readonly SolidColorBrush BrushRtp       = new(Color.FromRgb(0x4E, 0xC9, 0xB0)); // cyan — RTP debug

        public DebugWindow(WebRtcService webRtcService)
        {
            InitializeComponent();
            _webRtcService = webRtcService;

            // Clear the default empty paragraph WPF inserts
            SipLog.Document.Blocks.Clear();

            // Subscribe before replaying history so new events aren't missed
            _webRtcService.RegistrationStateChanged += OnRegistrationStateChanged;
            _webRtcService.CallStateChanged         += OnCallStateChanged;
            _webRtcService.SipMessageLogged         += OnSipMessageLogged;
            _webRtcService.RtpDebugLogged           += OnRtpDebugLogged;
            _webRtcService.MicLevelChanged          += OnMicLevelChanged;
            _webRtcService.SpeakerLevelChanged      += OnSpeakerLevelChanged;

            RefreshStatus();

            // Replay buffered SIP traffic so registration messages are visible
            // even when the window is opened after registration already completed
            AppendInfo("=== SIP log — replaying history ===");
            foreach (var entry in _webRtcService.GetSipLogHistory())
                AppendSip(entry.Direction, entry.Message);
            AppendInfo("=== live SIP traffic below ===");

            // Replay any buffered RTP events (e.g. call already in progress)
            foreach (var entry in _webRtcService.GetRtpLogHistory())
                AppendRtp(entry.Message);
        }

        // ── Event handlers ────────────────────────────────────────────────────────

        private void OnRegistrationStateChanged(object? sender, RegistrationState state)
            => Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(RefreshStatus));

        private void OnCallStateChanged(object? sender, CallState state)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                RefreshStatus();
                var reason = state == CallState.Failed
                    ? $": {_webRtcService.LastCallFailureReason ?? "unknown"}"
                    : "";
                AppendInfo($"Call state → {state}{reason}");
            }));
        }

        private void OnSipMessageLogged(object? sender, SipLogEventArgs e)
        {
            var dir = e.Direction;
            var msg = e.Message;
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => AppendSip(dir, msg)));
        }

        private void OnRtpDebugLogged(object? sender, RtpLogEventArgs e)
        {
            var msg = e.Message;
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => AppendRtp(msg)));
        }

        private void OnMicLevelChanged(object? sender, float level)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                var pct = (int)(level * 100);
                MicLevelBar.Value = pct;
                MicLevelText.Text = $"{pct}%";
            }));
        }

        private void OnSpeakerLevelChanged(object? sender, float level)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                var pct = (int)(level * 100);
                SpeakerLevelBar.Value = pct;
                SpeakerLevelText.Text = $"{pct}%";
            }));
        }

        // ── Button handlers ───────────────────────────────────────────────────────

        private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshStatus();

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            SipLog.Document.Blocks.Clear();
            AppendInfo("Log cleared.");
        }

        // ── Status panel ──────────────────────────────────────────────────────────

        private void RefreshStatus()
        {
            var cfg  = _webRtcService.GetConfiguration();
            var call = _webRtcService.GetCurrentCall();
            var publicIp = _webRtcService.GetPublicIp();
            var stats = _webRtcService.GetRtpStats();

            var sb = new StringBuilder();
            sb.AppendLine($"Registration : {_webRtcService.RegistrationState}  |  {_webRtcService.RegistrationMessage}");
            sb.AppendLine($"Signaling URL: {cfg.SignalingServerUrl ?? "(not set)"}  |  User: {cfg.Username ?? "(not set)"}");
            sb.AppendLine($"STUN: {cfg.StunServer ?? "(not set)"}  |  TURN: {cfg.TurnServer ?? "(not set)"}  |  Codec: {cfg.AudioCodecName ?? "(not set)"}");
            sb.AppendLine($"Public IP    : {publicIp?.ToString() ?? "(not discovered)"}");

            if (call != null)
            {
                sb.AppendLine($"Active call  : {call.State}  |  Remote: {call.RemoteParty}  |  Error: {call.ErrorMessage ?? "—"}");
                sb.AppendLine($"RTP stats    : Sent: {stats.sent} pkts ({stats.bytesSent / 1024}KB)  |  Recv: {stats.recv} pkts ({stats.bytesRecv / 1024}KB)");
            }
            else
                sb.AppendLine("Active call  : none");

            if (!string.IsNullOrEmpty(_webRtcService.LastCallFailureReason))
                sb.AppendLine($"Last failure : {_webRtcService.LastCallFailureReason}");

            StatusText.Text = sb.ToString().TrimEnd();
        }

        // ── Log appenders ─────────────────────────────────────────────────────────

        private void AppendSip(string direction, string raw)
        {
            try
            {
                var isSent = direction == ">>";
                var normalized = (raw ?? "").Replace("\r\n", "\n").TrimEnd();
                var lines  = normalized.Split('\n');
                var first  = lines.Length > 0 ? lines[0].Trim() : "(empty)";

                bool isError = !isSent && (first.StartsWith("SIP/2.0 4") ||
                                           first.StartsWith("SIP/2.0 5") ||
                                           first.StartsWith("SIP/2.0 6"));

                var headerBrush = isError ? BrushError : isSent ? BrushSend : BrushRecv;

                var para = new Paragraph { Margin = new Thickness(0, 3, 0, 0), LineHeight = 16 };

                para.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss.fff}] ")
                    { Foreground = BrushTimestamp, FontSize = 10 });

                para.Inlines.Add(new Run($"{direction} {first}")
                    { Foreground = headerBrush, FontWeight = FontWeights.Bold });

                // Indent remaining lines (headers + SDP body)
                if (lines.Length > 1)
                {
                    var body = new StringBuilder();
                    for (int i = 1; i < lines.Length; i++)
                    {
                        var l = lines[i].TrimEnd('\r');
                        if (!string.IsNullOrEmpty(l))
                            body.Append($"\n    {l}");
                    }
                    if (body.Length > 0)
                        para.Inlines.Add(new Run(body.ToString()) { Foreground = BrushBody });
                }

                SipLog.Document.Blocks.Add(para);
                SipLog.ScrollToEnd();
            }
            catch (Exception ex)
            {
                AppendInfo($"[log error: {ex.Message}]");
            }
        }

        private void AppendRtp(string message)
        {
            try
            {
                var para = new Paragraph { Margin = new Thickness(0, 2, 0, 0), LineHeight = 15 };
                para.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss.fff}] ")
                    { Foreground = BrushTimestamp, FontSize = 10 });
                para.Inlines.Add(new Run("[RTP] ") { Foreground = BrushRtp, FontWeight = FontWeights.Bold });
                para.Inlines.Add(new Run(message) { Foreground = BrushRtp });
                SipLog.Document.Blocks.Add(para);
                SipLog.ScrollToEnd();
            }
            catch { /* swallow — never crash the window */ }
        }

        private void AppendInfo(string message)
        {
            try
            {
                var para = new Paragraph { Margin = new Thickness(0, 3, 0, 0), LineHeight = 16 };
                para.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss.fff}] ")
                    { Foreground = BrushTimestamp, FontSize = 10 });
                para.Inlines.Add(new Run(message) { Foreground = BrushInfo });
                SipLog.Document.Blocks.Add(para);
                SipLog.ScrollToEnd();
            }
            catch { /* swallow — never crash the window */ }
        }

        // ── Cleanup ───────────────────────────────────────────────────────────────

        protected override void OnClosed(EventArgs e)
        {
            _webRtcService.RegistrationStateChanged -= OnRegistrationStateChanged;
            _webRtcService.CallStateChanged         -= OnCallStateChanged;
            _webRtcService.SipMessageLogged         -= OnSipMessageLogged;
            _webRtcService.RtpDebugLogged           -= OnRtpDebugLogged;
            _webRtcService.MicLevelChanged          -= OnMicLevelChanged;
            _webRtcService.SpeakerLevelChanged      -= OnSpeakerLevelChanged;
            base.OnClosed(e);
        }
    }
}
