using System;
using System.ComponentModel;
using System.IO;
using DrawingIcon = System.Drawing.Icon;
using SystemIcons = System.Drawing.SystemIcons;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WebRtcPhoneDialer.Core.Enums;
using WebRtcPhoneDialer.Core.Interfaces;
using WebRtcPhoneDialer.Core.Ipc;
using WebRtcPhoneDialer.Core.Models;
using WebRtcPhoneDialer.Core.Services;
using WebRtcPhoneDialer.ViewModels;
using WebRtcPhoneDialer.Windows;
using WinForms = System.Windows.Forms;

namespace WebRtcPhoneDialer.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel _viewModel;
        private IPhoneService _webRtcService;
        private System.Windows.Threading.DispatcherTimer _callTimer;
        private AppSettings _settings;
        private CallSession? _currentCall;
        private IncomingCallWindow? _incomingCallPopup;
        private bool _ownsService;
        private WinForms.NotifyIcon? _trayIcon;
        private bool _forceClose;
        private PhoneIpcServer? _ipcServer;

        /// <summary>Standalone mode — creates its own service.</summary>
        public MainWindow() : this(null) { }

        /// <summary>
        /// Hosted mode — uses an externally provided IPhoneService.
        /// When external, the window does NOT dispose/unregister on close.
        /// All call actions go through the shared service so the parent sees events.
        /// </summary>
        public MainWindow(IPhoneService? externalService)
        {
            InitializeComponent();

            if (externalService != null)
            {
                _webRtcService = externalService;
                _ownsService = false;
                _settings = new AppSettings();
            }
            else
            {
                _webRtcService = new WebRtcService(new WindowsAudioEndPointFactory());
                _ownsService = true;
                _settings = AppSettings.Load();
                _webRtcService.Configure(_settings);
            }

            _viewModel = new MainWindowViewModel(_webRtcService);
            DataContext = _viewModel;

            _callTimer = new System.Windows.Threading.DispatcherTimer();
            _callTimer.Interval = TimeSpan.FromSeconds(1);
            _callTimer.Tick += CallTimer_Tick;

            // Subscribe to state change events
            _webRtcService.RegistrationStateChanged += OnRegistrationStateChanged;
            _webRtcService.CallStateChanged += OnCallStateChanged;
            _webRtcService.IncomingCall += OnIncomingCall;
            _webRtcService.IncomingCallCanceled += OnIncomingCallCanceled;
            _webRtcService.NetworkQualityChanged += OnNetworkQualityChanged;

            // Start IPC server so the SDK ExampleApp can connect and control this instance
            if (_ownsService)
            {
                _ipcServer = new PhoneIpcServer(_webRtcService);
                _ipcServer.ShowWindowRequested += (_, _) =>
                    Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
            }

            // Sync current state into UI (important when shared service is already registered)
            UpdateRegistrationStatus(_webRtcService.RegistrationState);

            // Placeholder visibility for phone input
            PhoneNumberInput.TextChanged += (_, _) =>
                InputPlaceholder.Visibility = string.IsNullOrEmpty(PhoneNumberInput.Text)
                    ? Visibility.Visible : Visibility.Collapsed;

            // System tray icon (always — works in both standalone and hosted mode)
            SetupTrayIcon();
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new WinForms.NotifyIcon();

            // Load icon from embedded resource
            try
            {
                var iconUri = new Uri("pack://application:,,,/WebRtcPhoneDialer;component/voipat.ico", UriKind.Absolute);
                var iconStream = System.Windows.Application.GetResourceStream(iconUri)?.Stream;
                if (iconStream != null)
                    _trayIcon.Icon = new DrawingIcon(iconStream);
            }
            catch
            {
                _trayIcon.Icon = SystemIcons.Application;
            }

            _trayIcon.Text = "VOIPAT Phone";
            _trayIcon.Visible = true;

            // Double-click tray icon → restore window
            _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

            // Right-click context menu
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Show VOIPAT Phone", null, (_, _) => RestoreFromTray());
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) =>
            {
                _forceClose = true;
                Close();
            });
            _trayIcon.ContextMenuStrip = menu;
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Minimized && _trayIcon != null)
            {
                Hide();
                _trayIcon.ShowBalloonTip(1500, "VOIPAT Phone", "Running in system tray", WinForms.ToolTipIcon.Info);
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Map key to dialer character
            char? digit = e.Key switch
            {
                Key.D0 or Key.NumPad0 => '0',
                Key.D1 or Key.NumPad1 => '1',
                Key.D2 or Key.NumPad2 => '2',
                Key.D3 or Key.NumPad3 => '3',
                Key.D4 or Key.NumPad4 => '4',
                Key.D5 or Key.NumPad5 => '5',
                Key.D6 or Key.NumPad6 => '6',
                Key.D7 or Key.NumPad7 => '7',
                Key.D8 or Key.NumPad8 => '8',
                Key.D9 or Key.NumPad9 => '9',
                Key.Multiply => '*',
                Key.OemPlus when (Keyboard.Modifiers & ModifierKeys.Shift) != 0 => '+',
                _ => null
            };

            // Shift+3 = # on US keyboard
            if (e.Key == Key.D3 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                digit = '#';
            // Shift+8 = * on US keyboard
            if (e.Key == Key.D8 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                digit = '*';

            if (digit != null)
            {
                // Don't double-type if the TextBox already has focus
                if (!PhoneNumberInput.IsFocused)
                    PhoneNumberInput.Text += digit;
                // Send DTMF during active call
                if (_webRtcService.HasActiveCall)
                {
                    try { _webRtcService.SendDtmf((byte)digit.Value); } catch { }
                }
                return;
            }

            // Enter → Call or Hangup
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (HangupButton.IsEnabled)
                    HangupButton_Click(this, new RoutedEventArgs());
                else if (CallButton.IsEnabled && !string.IsNullOrWhiteSpace(PhoneNumberInput.Text))
                    CallButton_Click(this, new RoutedEventArgs());
                return;
            }

            // Escape → Hangup active call
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (HangupButton.IsEnabled)
                    HangupButton_Click(this, new RoutedEventArgs());
                return;
            }

            // Backspace → delete last char (when TextBox not focused)
            if (e.Key == Key.Back && !PhoneNumberInput.IsFocused)
            {
                if (PhoneNumberInput.Text.Length > 0)
                    PhoneNumberInput.Text = PhoneNumberInput.Text.Substring(0, PhoneNumberInput.Text.Length - 1);
                e.Handled = true;
                return;
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_settings, _webRtcService)
            {
                Owner = this
            };
            if (settingsWindow.ShowDialog() == true)
            {
                _settings = AppSettings.Load();
            }
        }

        private void DialerButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null && button.Content != null)
            {
                PhoneNumberInput.Text += button.Content.ToString();
            }
        }

        private async void CallButton_Click(object sender, RoutedEventArgs e)
        {
            string phoneNumber = PhoneNumberInput.Text.Trim();
            if (string.IsNullOrEmpty(phoneNumber))
            {
                MessageBox.Show("Please enter a phone number or SIP URI.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CallButton.IsEnabled = false;
            HangupButton.IsEnabled = true;
            CallStatusText.Text = $"Calling {phoneNumber}...";
            CallStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xAB, 0x00));

            _currentCall = new CallSession
            {
                RemoteParty = phoneNumber,
                StartTime = DateTime.Now,
                State = CallState.Initiating
            };

            try
            {
                await _webRtcService.InitiateCallAsync(phoneNumber);
            }
            catch (Exception ex)
            {
                _currentCall.State = CallState.Failed;
                _currentCall.EndTime = DateTime.Now;
                _currentCall.ErrorMessage = ex.Message;
                _viewModel.AddCallToHistory(_currentCall);
                _currentCall = null;

                CallStatusText.Text = "Call failed";
                CallButton.IsEnabled = true;
                HangupButton.IsEnabled = false;
                MessageBox.Show($"Error initiating call: {ex.Message}", "Call Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void HangupButton_Click(object sender, RoutedEventArgs e)
        {
            HangupButton.IsEnabled = false;
            HoldButton.IsEnabled = false;
            try
            {
                await _webRtcService.EndCallAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error ending call: {ex.Message}", "Call Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HoldButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var call = _webRtcService.GetCurrentCall();
                if (call?.State == CallState.OnHold)
                {
                    _webRtcService.UnholdCall();
                }
                else if (call?.State == CallState.Connected)
                {
                    _webRtcService.HoldCall();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Hold Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnIncomingCall(object? sender, CallSession call)
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    _currentCall = call;

                    // Restore from tray so the incoming call popup is visible
                    if (WindowState == WindowState.Minimized || !IsVisible)
                        RestoreFromTray();

                    _incomingCallPopup = new IncomingCallWindow(call.RemoteParty, _settings);
                    if (IsVisible)
                        _incomingCallPopup.Owner = this;
                    var answered = _incomingCallPopup.ShowDialog() == true && _incomingCallPopup.Answered;
                    _incomingCallPopup = null;

                    if (answered)
                    {
                        _ = AnswerIncomingCallAsync();
                    }
                    else
                    {
                        // Only reject if not already canceled by the remote caller
                        if (_webRtcService.GetCurrentCall()?.State == CallState.Ringing)
                            _webRtcService.RejectCall();
                        _currentCall = null;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"OnIncomingCall error: {ex.Message}");
                    _incomingCallPopup = null;
                    try { _webRtcService.RejectCall(); } catch { }
                    _currentCall = null;
                }
            });
        }

        private void OnIncomingCallCanceled(object? sender, EventArgs e)
        {
            // Caller hung up while ringing — dismiss the popup
            Dispatcher.BeginInvoke(() =>
            {
                _incomingCallPopup?.Close();
                _incomingCallPopup = null;
                _currentCall = null;
            });
        }

        private async Task AnswerIncomingCallAsync()
        {
            try
            {
                CallButton.IsEnabled = false;
                HangupButton.IsEnabled = true;
                HoldButton.IsEnabled = false;
                CallStatusText.Text = "Answering...";
                CallStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xAB, 0x00));
                await _webRtcService.AnswerCallAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error answering call: {ex.Message}", "Call Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CallButton.IsEnabled = true;
                HangupButton.IsEnabled = false;
                _currentCall = null;
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            PhoneNumberInput.Clear();
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ClearCallHistory();
        }

        private void CallHistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (CallHistoryList.SelectedItem is CallSession call)
            {
                DialFromHistory(call.RemoteParty);
            }
        }

        private void HistoryDial_Click(object sender, RoutedEventArgs e)
        {
            if (CallHistoryList.SelectedItem is CallSession call)
            {
                DialFromHistory(call.RemoteParty);
            }
        }

        private void HistoryCopyNumber_Click(object sender, RoutedEventArgs e)
        {
            if (CallHistoryList.SelectedItem is CallSession call)
            {
                Clipboard.SetText(call.RemoteParty);
            }
        }

        private void HistoryRemove_Click(object sender, RoutedEventArgs e)
        {
            if (CallHistoryList.SelectedItem is CallSession call)
            {
                _viewModel.CallHistory.Remove(call);
            }
        }

        private void DialFromHistory(string number)
        {
            PhoneNumberInput.Text = number;
            CallButton_Click(this, new RoutedEventArgs());
        }

        private void CallTimer_Tick(object? sender, EventArgs e)
        {
            var callDuration = _webRtcService.GetCallDuration();
            CallDurationText.Text = $"{callDuration.Hours:D2}:{callDuration.Minutes:D2}:{callDuration.Seconds:D2}";
        }

        private async void Register_Click(object sender, RoutedEventArgs e)
        {
            RegisterButton.IsEnabled = false;
            try
            {
                await _webRtcService.RegisterAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Registration error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                RegisterButton.IsEnabled = true;
            }
        }

        private void Debug_Click(object sender, RoutedEventArgs e)
        {
            var debugWindow = new DebugWindow(_webRtcService) { Owner = this };
            debugWindow.Show();
        }

        private void OnRegistrationStateChanged(object? sender, RegistrationState state)
        {
            Dispatcher.Invoke(() => UpdateRegistrationStatus(state));
        }

        private void UpdateRegistrationStatus(RegistrationState state)
        {
            switch (state)
            {
                case RegistrationState.Unregistered:
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x66));
                    StatusText.Text = "Unregistered";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0xAA));
                    RegisterButton.IsEnabled = true;
                    break;
                case RegistrationState.Registering:
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xAB, 0x00));
                    StatusText.Text = "Registering...";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xAB, 0x00));
                    RegisterButton.IsEnabled = false;
                    break;
                case RegistrationState.Registered:
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                    StatusText.Text = "Registered";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                    RegisterButton.IsEnabled = true;
                    break;
                case RegistrationState.Failed:
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x17, 0x44));
                    StatusText.Text = "Failed";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x17, 0x44));
                    RegisterButton.IsEnabled = true;
                    break;
            }
        }

        private void OnCallStateChanged(object? sender, CallState state)
        {
            Dispatcher.BeginInvoke(() => UpdateCallStatus(state));
        }

        private void UpdateCallStatus(CallState state)
        {
            if (_currentCall != null)
                _currentCall.State = state;

            switch (state)
            {
                case CallState.Initiating:
                    CallStatusText.Text = "Initiating...";
                    CallStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xAB, 0x00));
                    CallDurationText.Text = "00:00:00";
                    CallButton.IsEnabled = false;
                    HangupButton.IsEnabled = true;
                    _callTimer.Stop();
                    break;
                case CallState.Ringing:
                    CallStatusText.Text = "Ringing...";
                    CallStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xAB, 0x00));
                    break;
                case CallState.Connected:
                    CallStatusText.Text = "Connected";
                    CallStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
                    CallDurationText.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xEE));
                    HoldButton.IsEnabled = true;
                    HoldButtonText.Text = "\u23F8"; // pause icon
                    _callTimer.Start();
                    NetQualityPanel.Visibility = Visibility.Visible;
                    break;
                case CallState.OnHold:
                    CallStatusText.Text = "On Hold";
                    CallStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xAB, 0x00));
                    HoldButton.IsEnabled = true;
                    HoldButtonText.Text = "\u25B6"; // play icon (resume)
                    break;
                case CallState.Ended:
                    _callTimer.Stop();
                    NetQualityPanel.Visibility = Visibility.Collapsed;
                    if (_currentCall != null)
                    {
                        _currentCall.EndTime = DateTime.Now;
                        _viewModel.AddCallToHistory(_currentCall);
                        _currentCall = null;
                    }
                    CallStatusText.Text = "Call ended";
                    CallStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xEE));
                    CallDurationText.Text = "00:00:00";
                    CallDurationText.Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0x6C, 0x8A));
                    CallButton.IsEnabled = true;
                    HangupButton.IsEnabled = false;
                    HoldButton.IsEnabled = false;
                    break;
                case CallState.Failed:
                    _callTimer.Stop();
                    NetQualityPanel.Visibility = Visibility.Collapsed;
                    if (_currentCall != null)
                    {
                        _currentCall.EndTime = DateTime.Now;
                        _viewModel.AddCallToHistory(_currentCall);
                        _currentCall = null;
                    }
                    CallStatusText.Text = "Call failed";
                    CallStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x17, 0x44));
                    CallDurationText.Text = "00:00:00";
                    CallDurationText.Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0x6C, 0x8A));
                    CallButton.IsEnabled = true;
                    HangupButton.IsEnabled = false;
                    HoldButton.IsEnabled = false;
                    break;
            }
        }

        private void OnNetworkQualityChanged(object? sender, NetworkQualityMetrics m)
        {
            Dispatcher.BeginInvoke(() => UpdateNetworkQualityPanel(m));
        }

        private void UpdateNetworkQualityPanel(NetworkQualityMetrics m)
        {
            // Quality color and label
            var (barColor, labelColor, label) = m.Quality switch
            {
                NetworkCallQuality.Excellent => (Color.FromRgb(0x00, 0xE6, 0x76), Color.FromRgb(0x00, 0xE6, 0x76), "Excellent"),
                NetworkCallQuality.Good      => (Color.FromRgb(0x76, 0xFF, 0x03), Color.FromRgb(0x76, 0xFF, 0x03), "Good"),
                NetworkCallQuality.Fair      => (Color.FromRgb(0xFF, 0xAB, 0x00), Color.FromRgb(0xFF, 0xAB, 0x00), "Fair"),
                NetworkCallQuality.Poor      => (Color.FromRgb(0xFF, 0x17, 0x44), Color.FromRgb(0xFF, 0x17, 0x44), "Poor"),
                NetworkCallQuality.NoMedia   => (Color.FromRgb(0x55, 0x55, 0x66), Color.FromRgb(0x55, 0x55, 0x66), "No Media"),
                _                            => (Color.FromRgb(0x33, 0x33, 0x50), Color.FromRgb(0x55, 0x55, 0x66), "—"),
            };

            int bars = m.Quality switch
            {
                NetworkCallQuality.Excellent => 4,
                NetworkCallQuality.Good      => 3,
                NetworkCallQuality.Fair      => 2,
                NetworkCallQuality.Poor      => 1,
                _                            => 0,
            };

            var dimColor = Color.FromRgb(0x33, 0x33, 0x50);
            QBar1.Fill = new SolidColorBrush(bars >= 1 ? barColor : dimColor);
            QBar2.Fill = new SolidColorBrush(bars >= 2 ? barColor : dimColor);
            QBar3.Fill = new SolidColorBrush(bars >= 3 ? barColor : dimColor);
            QBar4.Fill = new SolidColorBrush(bars >= 4 ? barColor : dimColor);

            // Codec suffix
            var codec = string.IsNullOrEmpty(m.Codec) ? "" : $"  [{m.Codec}]";
            NetQualityText.Text = label + codec;
            NetQualityText.Foreground = new SolidColorBrush(labelColor);

            var dimFg = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x77));
            var veryDimFg = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x50));

            if (m.HasMedia)
            {
                NetStatsText.Text = $"Loss: {m.PacketLossPct:F1}%   Jitter: {m.JitterMs:F0} ms";
                NetStatsText.Foreground = dimFg;

                NetRxRateText.Text = $"↓ {(m.RxKbps > 0 ? m.RxKbps + " kbps" : m.RxPps + " pps")}";
                NetTxRateText.Text = $"↑ {(m.TxKbps > 0 ? m.TxKbps + " kbps" : m.TxPps + " pps")}";
                NetRxRateText.Foreground = dimFg;
                NetTxRateText.Foreground = dimFg;
            }
            else
            {
                NetStatsText.Text = "Awaiting RTP...";
                NetStatsText.Foreground = veryDimFg;
                NetRxRateText.Text = "↓ --";
                NetTxRateText.Text = "↑ --";
                NetRxRateText.Foreground = veryDimFg;
                NetTxRateText.Foreground = veryDimFg;
            }
        }

        private async void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_webRtcService == null) return;

            // Minimize to tray instead of closing (unless forced)
            if (!_forceClose && _trayIcon != null)
            {
                e.Cancel = true;
                WindowState = WindowState.Minimized;
                return;
            }

            // Unsubscribe events regardless of ownership
            _webRtcService.RegistrationStateChanged -= OnRegistrationStateChanged;
            _webRtcService.CallStateChanged -= OnCallStateChanged;
            _webRtcService.IncomingCall -= OnIncomingCall;
            _webRtcService.IncomingCallCanceled -= OnIncomingCallCanceled;
            _webRtcService.NetworkQualityChanged -= OnNetworkQualityChanged;
            _callTimer.Stop();

            // Dispose tray icon
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            _ipcServer?.Dispose();

            if (!_ownsService) return; // parent manages lifecycle

            // If there's an active call, ask for confirmation
            if (_webRtcService.HasActiveCall)
            {
                var result = MessageBox.Show(
                    "A call is currently active. Hang up and exit?",
                    "Active Call",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                try
                {
                    await _webRtcService.EndCallAsync();
                }
                catch { }
            }

            _webRtcService.Unregister();
            _webRtcService.Dispose();
        }
    }
}
