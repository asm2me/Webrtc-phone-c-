using System;
using System.Drawing;
using System.Windows.Forms;
using WebRtcPhoneDialer.Core.Enums;
using WebRtcPhoneDialer.Core.Events;
using WebRtcPhoneDialer.Core.Models;
using WebRtcPhoneDialer.Windows;

namespace ExampleApp
{
    public class MainForm : Form
    {
        private readonly PhoneDialerHost _phone;

        // ── Settings controls ──
        private TextBox _txtServer = null!;
        private TextBox _txtDomain = null!;
        private TextBox _txtUsername = null!;
        private TextBox _txtPassword = null!;
        private TextBox _txtStunServer = null!;
        private ComboBox _cboInputDevice = null!;
        private ComboBox _cboOutputDevice = null!;
        private Button _btnApplySettings = null!;

        // ── Registration controls ──
        private Button _btnRegister = null!;
        private Button _btnUnregister = null!;
        private Label _lblRegStatus = null!;

        // ── Call controls ──
        private TextBox _txtDialNumber = null!;
        private Button _btnCall = null!;
        private Button _btnHangup = null!;
        private Button _btnHold = null!;
        private Button _btnAnswer = null!;
        private Button _btnReject = null!;
        private Button _btnMute = null!;
        private Label _lblCallStatus = null!;
        private Label _lblCallDuration = null!;

        // ── DTMF ──
        private TableLayoutPanel _dtmfPanel = null!;

        // ── Audio levels ──
        private ProgressBar _micLevel = null!;
        private ProgressBar _spkLevel = null!;
        private Label _lblMic = null!;
        private Label _lblSpk = null!;

        // ── Network quality ──
        private Label _lblNetQuality = null!;
        private Label _lblNetStats = null!;

        // ── Debug log ──
        private TextBox _txtDebugLog = null!;
        private CheckBox _chkSipLog = null!;
        private CheckBox _chkRtpLog = null!;

        // ── State ──
        private bool _isMuted;
        private bool _isOnHold;
        private System.Windows.Forms.Timer _durationTimer = null!;

        public MainForm()
        {
            _phone = new PhoneDialerHost();
            InitializeUI();
            WireEvents();
            PopulateAudioDevices();
            LoadSettings();
        }

        private void InitializeUI()
        {
            Text = "VOIPAT Phone - Example App";
            Size = new Size(900, 750);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            MinimumSize = new Size(850, 700);

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(8)
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));

            // ── LEFT PANEL ──
            var leftPanel = new Panel { Dock = DockStyle.Fill };
            var leftScroll = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };

            // Settings Group
            var grpSettings = CreateGroup("Settings", 280);
            var settingsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8 };
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _txtServer = AddSettingRow(settingsLayout, "Server URL:", 0);
            _txtDomain = AddSettingRow(settingsLayout, "SIP Domain:", 1);
            _txtUsername = AddSettingRow(settingsLayout, "Username:", 2);
            _txtPassword = AddSettingRow(settingsLayout, "Password:", 3);
            _txtPassword.UseSystemPasswordChar = true;
            _txtStunServer = AddSettingRow(settingsLayout, "STUN Server:", 4);

            settingsLayout.Controls.Add(new Label { Text = "Input Device:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 5);
            _cboInputDevice = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            settingsLayout.Controls.Add(_cboInputDevice, 1, 5);

            settingsLayout.Controls.Add(new Label { Text = "Output Device:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 6);
            _cboOutputDevice = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            settingsLayout.Controls.Add(_cboOutputDevice, 1, 6);

            _btnApplySettings = new Button { Text = "Apply Settings", Dock = DockStyle.Fill, Height = 30 };
            _btnApplySettings.Click += BtnApplySettings_Click;
            settingsLayout.Controls.Add(_btnApplySettings, 0, 7);
            settingsLayout.SetColumnSpan(_btnApplySettings, 2);

            grpSettings.Controls.Add(settingsLayout);
            leftScroll.Controls.Add(grpSettings);

            // Registration Group
            var grpReg = CreateGroup("Registration", 70);
            var regLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _btnRegister = new Button { Text = "Register", Width = 90, Height = 32 };
            _btnRegister.Click += BtnRegister_Click;
            _btnUnregister = new Button { Text = "Unregister", Width = 90, Height = 32, Enabled = false };
            _btnUnregister.Click += BtnUnregister_Click;
            _lblRegStatus = new Label { Text = "Unregistered", AutoSize = true, ForeColor = Color.Gray, Padding = new Padding(6, 8, 0, 0) };
            regLayout.Controls.AddRange(new Control[] { _btnRegister, _btnUnregister, _lblRegStatus });
            grpReg.Controls.Add(regLayout);
            leftScroll.Controls.Add(grpReg);

            // Call Control Group
            var grpCall = CreateGroup("Call Control", 150);
            var callLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
            callLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            callLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

            _txtDialNumber = new TextBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 14f) };
            callLayout.Controls.Add(_txtDialNumber, 0, 0);
            _btnCall = new Button { Text = "Call", Dock = DockStyle.Fill, Height = 35, BackColor = Color.FromArgb(76, 175, 80), ForeColor = Color.White };
            _btnCall.Click += BtnCall_Click;
            callLayout.Controls.Add(_btnCall, 1, 0);

            var callBtnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _btnHangup = new Button { Text = "Hang Up", Width = 80, Height = 32, BackColor = Color.FromArgb(244, 67, 54), ForeColor = Color.White, Enabled = false };
            _btnHangup.Click += BtnHangup_Click;
            _btnHold = new Button { Text = "Hold", Width = 65, Height = 32, Enabled = false };
            _btnHold.Click += BtnHold_Click;
            _btnMute = new Button { Text = "Mute", Width = 65, Height = 32, Enabled = false };
            _btnMute.Click += BtnMute_Click;
            _btnAnswer = new Button { Text = "Answer", Width = 70, Height = 32, BackColor = Color.FromArgb(76, 175, 80), ForeColor = Color.White, Enabled = false };
            _btnAnswer.Click += BtnAnswer_Click;
            _btnReject = new Button { Text = "Reject", Width = 65, Height = 32, BackColor = Color.FromArgb(244, 67, 54), ForeColor = Color.White, Enabled = false };
            _btnReject.Click += BtnReject_Click;
            callBtnPanel.Controls.AddRange(new Control[] { _btnHangup, _btnHold, _btnMute, _btnAnswer, _btnReject });
            callLayout.Controls.Add(callBtnPanel, 0, 1);
            callLayout.SetColumnSpan(callBtnPanel, 2);

            _lblCallStatus = new Label { Text = "Idle", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = Color.Gray };
            callLayout.Controls.Add(_lblCallStatus, 0, 2);
            _lblCallDuration = new Label { Text = "00:00", Dock = DockStyle.Fill, Font = new Font("Consolas", 11f), TextAlign = ContentAlignment.MiddleRight };
            callLayout.Controls.Add(_lblCallDuration, 1, 2);

            grpCall.Controls.Add(callLayout);
            leftScroll.Controls.Add(grpCall);

            // DTMF Group
            var grpDtmf = CreateGroup("DTMF", 140);
            _dtmfPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4 };
            string[] dtmfLabels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "*", "0", "#" };
            byte[] dtmfTones = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 0, 11 };
            for (int i = 0; i < 12; i++)
            {
                var btn = new Button { Text = dtmfLabels[i], Width = 50, Height = 28, Tag = dtmfTones[i] };
                btn.Click += BtnDtmf_Click;
                _dtmfPanel.Controls.Add(btn, i % 3, i / 3);
            }
            grpDtmf.Controls.Add(_dtmfPanel);
            leftScroll.Controls.Add(grpDtmf);

            // Audio Levels Group
            var grpAudio = CreateGroup("Audio Levels", 70);
            var audioLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            audioLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            audioLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _lblMic = new Label { Text = "Mic:", AutoSize = true };
            _micLevel = new ProgressBar { Dock = DockStyle.Fill, Maximum = 100, Height = 16 };
            _lblSpk = new Label { Text = "Speaker:", AutoSize = true };
            _spkLevel = new ProgressBar { Dock = DockStyle.Fill, Maximum = 100, Height = 16 };
            audioLayout.Controls.Add(_lblMic, 0, 0);
            audioLayout.Controls.Add(_micLevel, 1, 0);
            audioLayout.Controls.Add(_lblSpk, 0, 1);
            audioLayout.Controls.Add(_spkLevel, 1, 1);
            grpAudio.Controls.Add(audioLayout);
            leftScroll.Controls.Add(grpAudio);

            // Network Quality Group
            var grpNet = CreateGroup("Network Quality", 80);
            var netLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            netLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            netLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            netLayout.Controls.Add(new Label { Text = "Quality:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            _lblNetQuality = new Label { Text = "—", AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.Gray };
            netLayout.Controls.Add(_lblNetQuality, 1, 0);
            netLayout.Controls.Add(new Label { Text = "Stats:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            _lblNetStats = new Label { Text = "—", AutoSize = true, ForeColor = Color.DimGray };
            netLayout.Controls.Add(_lblNetStats, 1, 1);
            grpNet.Controls.Add(netLayout);
            leftScroll.Controls.Add(grpNet);

            leftPanel.Controls.Add(leftScroll);
            mainLayout.Controls.Add(leftPanel, 0, 0);

            // ── RIGHT PANEL: Debug Log ──
            var rightPanel = new Panel { Dock = DockStyle.Fill };
            var grpDebug = new GroupBox { Text = "Debug Log", Dock = DockStyle.Fill };

            var debugTopPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, FlowDirection = FlowDirection.LeftToRight };
            _chkSipLog = new CheckBox { Text = "SIP Messages", Checked = true, AutoSize = true, Padding = new Padding(4) };
            _chkRtpLog = new CheckBox { Text = "RTP Debug", Checked = true, AutoSize = true, Padding = new Padding(4) };
            var btnClearLog = new Button { Text = "Clear", Width = 60, Height = 24 };
            btnClearLog.Click += (s, e) => _txtDebugLog.Clear();
            debugTopPanel.Controls.AddRange(new Control[] { _chkSipLog, _chkRtpLog, btnClearLog });

            _txtDebugLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 8.5f),
                WordWrap = false,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(200, 200, 200)
            };

            grpDebug.Controls.Add(_txtDebugLog);
            grpDebug.Controls.Add(debugTopPanel);
            rightPanel.Controls.Add(grpDebug);
            mainLayout.Controls.Add(rightPanel, 1, 0);

            Controls.Add(mainLayout);

            // Duration timer
            _durationTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _durationTimer.Tick += (s, e) =>
            {
                var dur = _phone.GetCallDuration();
                _lblCallDuration.Text = dur.Hours > 0
                    ? dur.ToString(@"h\:mm\:ss")
                    : dur.ToString(@"mm\:ss");
            };
        }

        private GroupBox CreateGroup(string title, int height)
        {
            return new GroupBox
            {
                Text = title,
                Width = 380,
                Height = height,
                Padding = new Padding(6)
            };
        }

        private TextBox AddSettingRow(TableLayoutPanel layout, string label, int row)
        {
            layout.Controls.Add(new Label { Text = label, Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
            var txt = new TextBox { Dock = DockStyle.Fill };
            layout.Controls.Add(txt, 1, row);
            return txt;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // EVENT WIRING
        // ═══════════════════════════════════════════════════════════════════════

        private void WireEvents()
        {
            _phone.RegistrationStateChanged += OnRegistrationStateChanged;
            _phone.CallStateDetailChanged += OnCallStateDetailChanged;
            _phone.IncomingCall += OnIncomingCall;
            _phone.MicLevelChanged += OnMicLevelChanged;
            _phone.SpeakerLevelChanged += OnSpeakerLevelChanged;
            _phone.SipMessageLogged += OnSipMessageLogged;
            _phone.RtpDebugLogged += OnRtpDebugLogged;
            _phone.NetworkQualityChanged += OnNetworkQualityChanged;
            _phone.IncomingCallCanceled += OnIncomingCallCanceled;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SETTINGS
        // ═══════════════════════════════════════════════════════════════════════

        private void PopulateAudioDevices()
        {
            _cboInputDevice.Items.Clear();
            _cboOutputDevice.Items.Clear();
            _cboInputDevice.Items.Add("(Default)");
            _cboOutputDevice.Items.Add("(Default)");

            foreach (var dev in _phone.AudioDevices.GetInputDevices())
                _cboInputDevice.Items.Add(dev);
            foreach (var dev in _phone.AudioDevices.GetOutputDevices())
                _cboOutputDevice.Items.Add(dev);

            _cboInputDevice.SelectedIndex = 0;
            _cboOutputDevice.SelectedIndex = 0;
        }

        private void LoadSettings()
        {
            var settings = _phone.LoadAndApplySettings();
            _txtServer.Text = settings.SignalingServerUrl;
            _txtDomain.Text = settings.SipDomain;
            _txtUsername.Text = settings.Username;
            _txtPassword.Text = settings.Password;
            _txtStunServer.Text = settings.StunServer;
        }

        private void BtnApplySettings_Click(object? sender, EventArgs e)
        {
            var settings = _phone.Settings;
            settings.SignalingServerUrl = _txtServer.Text.Trim();
            settings.SipDomain = _txtDomain.Text.Trim();
            settings.Username = _txtUsername.Text.Trim();
            settings.Password = _txtPassword.Text;
            settings.StunServer = _txtStunServer.Text.Trim();

            if (_cboInputDevice.SelectedItem is AudioDevice inputDev)
                settings.InputDeviceId = inputDev.Index.ToString();
            if (_cboOutputDevice.SelectedItem is AudioDevice outputDev)
                settings.OutputDeviceId = outputDev.Index.ToString();

            _phone.SaveAndApplySettings(settings);
            AppendLog("[SETTINGS] Configuration applied and saved.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // REGISTRATION
        // ═══════════════════════════════════════════════════════════════════════

        private async void BtnRegister_Click(object? sender, EventArgs e)
        {
            try
            {
                _btnRegister.Enabled = false;
                AppendLog("[REG] Registering...");
                await _phone.RegisterAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"[REG ERROR] {ex.Message}");
                _btnRegister.Enabled = true;
            }
        }

        private void BtnUnregister_Click(object? sender, EventArgs e)
        {
            _phone.Unregister();
            AppendLog("[REG] Unregistered.");
        }

        private void OnRegistrationStateChanged(object? sender, RegistrationState state)
        {
            if (InvokeRequired) { Invoke(() => OnRegistrationStateChanged(sender, state)); return; }

            _lblRegStatus.Text = state.ToString();
            switch (state)
            {
                case RegistrationState.Registered:
                    _lblRegStatus.ForeColor = Color.Green;
                    _btnRegister.Enabled = false;
                    _btnUnregister.Enabled = true;
                    break;
                case RegistrationState.Failed:
                    _lblRegStatus.ForeColor = Color.Red;
                    _lblRegStatus.Text = $"Failed: {_phone.RegistrationMessage}";
                    _btnRegister.Enabled = true;
                    _btnUnregister.Enabled = false;
                    break;
                case RegistrationState.Registering:
                    _lblRegStatus.ForeColor = Color.Orange;
                    break;
                case RegistrationState.Unregistered:
                    _lblRegStatus.ForeColor = Color.Gray;
                    _btnRegister.Enabled = true;
                    _btnUnregister.Enabled = false;
                    break;
            }
            AppendLog($"[REG] State: {state} - {_phone.RegistrationMessage}");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CALL CONTROL
        // ═══════════════════════════════════════════════════════════════════════

        private async void BtnCall_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_txtDialNumber.Text)) return;
            try
            {
                _btnCall.Enabled = false;
                AppendLog($"[CALL] Dialing {_txtDialNumber.Text}...");
                await _phone.InitiateCallAsync(_txtDialNumber.Text.Trim());
            }
            catch (Exception ex)
            {
                AppendLog($"[CALL ERROR] {ex.Message}");
                _btnCall.Enabled = true;
            }
        }

        private async void BtnHangup_Click(object? sender, EventArgs e)
        {
            try
            {
                AppendLog("[CALL] Hanging up...");
                await _phone.EndCallAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"[HANGUP ERROR] {ex.Message}");
            }
        }

        private void BtnHold_Click(object? sender, EventArgs e)
        {
            try
            {
                if (_isOnHold)
                {
                    _phone.UnholdCall();
                    AppendLog("[CALL] Resumed from hold.");
                }
                else
                {
                    _phone.HoldCall();
                    AppendLog("[CALL] Placed on hold.");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[HOLD ERROR] {ex.Message}");
            }
        }

        private void BtnMute_Click(object? sender, EventArgs e)
        {
            if (_isMuted)
            {
                _phone.UnmuteMicrophone();
                _btnMute.Text = "Mute";
                _isMuted = false;
                AppendLog("[AUDIO] Mic unmuted.");
            }
            else
            {
                _phone.MuteMicrophone();
                _btnMute.Text = "Unmute";
                _isMuted = true;
                AppendLog("[AUDIO] Mic muted.");
            }
        }

        private async void BtnAnswer_Click(object? sender, EventArgs e)
        {
            try
            {
                AppendLog("[CALL] Answering incoming call...");
                await _phone.AnswerCallAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"[ANSWER ERROR] {ex.Message}");
            }
        }

        private void BtnReject_Click(object? sender, EventArgs e)
        {
            _phone.RejectCall();
            AppendLog("[CALL] Incoming call rejected.");
        }

        private void BtnDtmf_Click(object? sender, EventArgs e)
        {
            if (sender is Button btn && btn.Tag is byte tone)
            {
                try
                {
                    _phone.SendDtmf(tone);
                    AppendLog($"[DTMF] Sent tone: {btn.Text}");
                }
                catch (Exception ex)
                {
                    AppendLog($"[DTMF ERROR] {ex.Message}");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CALL STATE EVENTS
        // ═══════════════════════════════════════════════════════════════════════

        private void OnCallStateDetailChanged(object? sender, CallStateChangedEventArgs e)
        {
            if (InvokeRequired) { Invoke(() => OnCallStateDetailChanged(sender, e)); return; }

            _lblCallStatus.Text = e.NewState.ToString();
            AppendLog($"[CALL] {e.PreviousState} -> {e.NewState} | Remote: {e.Call.RemoteParty}" +
                      (e.Reason != null ? $" | Reason: {e.Reason}" : ""));

            switch (e.NewState)
            {
                case CallState.Initiating:
                    _lblCallStatus.ForeColor = Color.Orange;
                    _btnCall.Enabled = false;
                    _btnHangup.Enabled = true;
                    break;

                case CallState.Ringing:
                    _lblCallStatus.ForeColor = Color.Orange;
                    break;

                case CallState.Connected:
                    _lblCallStatus.ForeColor = Color.Green;
                    _btnHold.Enabled = true;
                    _btnMute.Enabled = true;
                    _btnHangup.Enabled = true;
                    _btnAnswer.Enabled = false;
                    _btnReject.Enabled = false;
                    _isOnHold = false;
                    _btnHold.Text = "Hold";
                    _durationTimer.Start();
                    break;

                case CallState.OnHold:
                    _lblCallStatus.ForeColor = Color.FromArgb(255, 165, 0);
                    _lblCallStatus.Text = "On Hold";
                    _isOnHold = true;
                    _btnHold.Text = "Resume";
                    break;

                case CallState.Ended:
                case CallState.Failed:
                    _lblCallStatus.ForeColor = e.NewState == CallState.Failed ? Color.Red : Color.Gray;
                    if (e.NewState == CallState.Failed)
                        _lblCallStatus.Text = $"Failed: {e.Reason ?? _phone.LastCallFailureReason}";
                    ResetCallUI();
                    break;
            }
        }

        private void OnIncomingCall(object? sender, CallSession call)
        {
            if (InvokeRequired) { Invoke(() => OnIncomingCall(sender, call)); return; }

            AppendLog($"[INCOMING] Call from: {call.RemoteParty}");
            _lblCallStatus.Text = $"Incoming: {call.RemoteParty}";
            _lblCallStatus.ForeColor = Color.DodgerBlue;
            _btnAnswer.Enabled = true;
            _btnReject.Enabled = true;
            _btnCall.Enabled = false;
        }

        private void ResetCallUI()
        {
            _durationTimer.Stop();
            _btnCall.Enabled = true;
            _btnHangup.Enabled = false;
            _btnHold.Enabled = false;
            _btnMute.Enabled = false;
            _btnAnswer.Enabled = false;
            _btnReject.Enabled = false;
            _isOnHold = false;
            _isMuted = false;
            _btnHold.Text = "Hold";
            _btnMute.Text = "Mute";
            _lblCallDuration.Text = "00:00";
        }

        // ═══════════════════════════════════════════════════════════════════════
        // AUDIO LEVEL EVENTS
        // ═══════════════════════════════════════════════════════════════════════

        private void OnMicLevelChanged(object? sender, float level)
        {
            if (InvokeRequired) { BeginInvoke(() => _micLevel.Value = Math.Min(100, (int)(level * 100))); return; }
            _micLevel.Value = Math.Min(100, (int)(level * 100));
        }

        private void OnSpeakerLevelChanged(object? sender, float level)
        {
            if (InvokeRequired) { BeginInvoke(() => _spkLevel.Value = Math.Min(100, (int)(level * 100))); return; }
            _spkLevel.Value = Math.Min(100, (int)(level * 100));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // DEBUG LOG EVENTS
        // ═══════════════════════════════════════════════════════════════════════

        private void OnSipMessageLogged(object? sender, SipLogEventArgs e)
        {
            if (!_chkSipLog.Checked) return;
            AppendLog($"[SIP {e.Direction}] {e.Message.Substring(0, Math.Min(e.Message.Length, 200))}");
        }

        private void OnRtpDebugLogged(object? sender, RtpLogEventArgs e)
        {
            if (!_chkRtpLog.Checked) return;
            AppendLog($"[RTP] {e.Message}");
        }

        private void AppendLog(string text)
        {
            if (InvokeRequired) { BeginInvoke(() => AppendLog(text)); return; }

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            _txtDebugLog.AppendText($"[{timestamp}] {text}{Environment.NewLine}");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // NETWORK QUALITY
        // ═══════════════════════════════════════════════════════════════════════

        private void OnNetworkQualityChanged(object? sender, NetworkQualityMetrics m)
        {
            if (InvokeRequired) { BeginInvoke(() => OnNetworkQualityChanged(sender, m)); return; }

            var (color, label) = m.Quality switch
            {
                NetworkCallQuality.Excellent => (Color.Green,        "Excellent"),
                NetworkCallQuality.Good      => (Color.YellowGreen,  "Good"),
                NetworkCallQuality.Fair      => (Color.Orange,       "Fair"),
                NetworkCallQuality.Poor      => (Color.Red,          "Poor"),
                NetworkCallQuality.NoMedia   => (Color.Gray,         "No Media"),
                _                           => (Color.Gray,          "Unknown"),
            };

            _lblNetQuality.ForeColor = color;
            _lblNetQuality.Text = label + (m.Codec != null ? $" ({m.Codec})" : "");
            _lblNetStats.Text = $"Loss: {m.PacketLossPct:F1}%  Jitter: {m.JitterMs:F0}ms  ↓{m.RxKbps}kbps ↑{m.TxKbps}kbps";

            AppendLog($"[QUALITY] {label}  Loss={m.PacketLossPct:F1}%  Jitter={m.JitterMs:F0}ms  " +
                      $"↓{m.RxKbps}kbps ↑{m.TxKbps}kbps  Rx={m.RxPps}pps  Codec={m.Codec}");
        }

        private void OnIncomingCallCanceled(object? sender, EventArgs e)
        {
            if (InvokeRequired) { Invoke(() => OnIncomingCallCanceled(sender, e)); return; }
            AppendLog("[INCOMING] Caller hung up while ringing.");
            _lblCallStatus.Text = "Idle";
            _lblCallStatus.ForeColor = Color.Gray;
            ResetCallUI();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CLEANUP
        // ═══════════════════════════════════════════════════════════════════════

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _durationTimer.Stop();
            if (_phone.IsRegistered)
                _phone.Unregister();
            _phone.Dispose();
            base.OnFormClosing(e);
        }
    }
}
