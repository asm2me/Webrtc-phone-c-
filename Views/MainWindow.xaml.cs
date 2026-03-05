using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WebRtcPhoneDialer.Core.Enums;
using WebRtcPhoneDialer.Core.Interfaces;
using WebRtcPhoneDialer.Core.Models;
using WebRtcPhoneDialer.Core.Services;
using WebRtcPhoneDialer.ViewModels;
using WebRtcPhoneDialer.Windows;

namespace WebRtcPhoneDialer.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel _viewModel;
        private IPhoneService _webRtcService;
        private System.Windows.Threading.DispatcherTimer _callTimer;
        private AppSettings _settings;
        private CallSession? _currentCall;
        private bool _ownsService;

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

            // Sync current state into UI (important when shared service is already registered)
            UpdateRegistrationStatus(_webRtcService.RegistrationState);

            // Placeholder visibility for phone input
            PhoneNumberInput.TextChanged += (_, _) =>
                InputPlaceholder.Visibility = string.IsNullOrEmpty(PhoneNumberInput.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
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
            Dispatcher.Invoke(() =>
            {
                _currentCall = call;
                var result = MessageBox.Show(
                    $"Incoming call from {call.RemoteParty}\n\nAnswer?",
                    "Incoming Call",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _ = AnswerIncomingCallAsync();
                }
                else
                {
                    _webRtcService.RejectCall();
                    _currentCall = null;
                }
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
            Dispatcher.Invoke(() => UpdateCallStatus(state));
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
                    break;
                case CallState.OnHold:
                    CallStatusText.Text = "On Hold";
                    CallStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xAB, 0x00));
                    HoldButton.IsEnabled = true;
                    HoldButtonText.Text = "\u25B6"; // play icon (resume)
                    break;
                case CallState.Ended:
                    _callTimer.Stop();
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

        private async void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_webRtcService == null) return;

            // Unsubscribe events regardless of ownership
            _webRtcService.RegistrationStateChanged -= OnRegistrationStateChanged;
            _webRtcService.CallStateChanged -= OnCallStateChanged;
            _webRtcService.IncomingCall -= OnIncomingCall;
            _callTimer.Stop();

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
