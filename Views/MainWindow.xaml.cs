using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WebRtcPhoneDialer.Models;
using WebRtcPhoneDialer.Services;
using WebRtcPhoneDialer.ViewModels;

namespace WebRtcPhoneDialer.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel _viewModel;
        private WebRtcService _webRtcService;
        private System.Windows.Threading.DispatcherTimer _callTimer;
        private AppSettings _settings;

        public MainWindow()
        {
            InitializeComponent();
            _webRtcService = new WebRtcService();
            _viewModel = new MainWindowViewModel(_webRtcService);
            DataContext = _viewModel;

            _callTimer = new System.Windows.Threading.DispatcherTimer();
            _callTimer.Interval = TimeSpan.FromSeconds(1);
            _callTimer.Tick += CallTimer_Tick;

            // Load and apply saved settings on startup
            _settings = AppSettings.Load();
            _webRtcService.Configure(_settings);

            // Subscribe to state change events
            _webRtcService.RegistrationStateChanged += OnRegistrationStateChanged;
            _webRtcService.CallStateChanged += OnCallStateChanged;

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

            try
            {
                await _webRtcService.InitiateCallAsync(phoneNumber);
            }
            catch (Exception ex)
            {
                CallStatusText.Text = "Call failed";
                CallButton.IsEnabled = true;
                HangupButton.IsEnabled = false;
                MessageBox.Show($"Error initiating call: {ex.Message}", "Call Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void HangupButton_Click(object sender, RoutedEventArgs e)
        {
            HangupButton.IsEnabled = false;
            try
            {
                await _webRtcService.EndCallAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error ending call: {ex.Message}", "Call Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            PhoneNumberInput.Clear();
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
                    _callTimer.Start();
                    break;
                case CallState.Ended:
                    _callTimer.Stop();
                    CallStatusText.Text = "Call ended";
                    CallStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xEE));
                    CallDurationText.Text = "00:00:00";
                    CallDurationText.Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0x6C, 0x8A));
                    CallButton.IsEnabled = true;
                    HangupButton.IsEnabled = false;
                    break;
                case CallState.Failed:
                    _callTimer.Stop();
                    CallStatusText.Text = "Call failed";
                    CallStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x17, 0x44));
                    CallDurationText.Text = "00:00:00";
                    CallDurationText.Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0x6C, 0x8A));
                    CallButton.IsEnabled = true;
                    HangupButton.IsEnabled = false;
                    break;
            }
        }

        private async void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_webRtcService == null) return;

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

                // Hang up the active call
                try
                {
                    await _webRtcService.EndCallAsync();
                }
                catch { }
            }

            // Unregister from SIP server
            _webRtcService.Unregister();

            // Dispose all resources
            _webRtcService.Dispose();
        }
    }
}
