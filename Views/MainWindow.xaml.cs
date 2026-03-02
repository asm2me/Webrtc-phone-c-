using System;
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
            CallDurationText.Text = $"Duration: {callDuration.Hours:D2}:{callDuration.Minutes:D2}:{callDuration.Seconds:D2}";
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
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
                    StatusText.Text = "Unregistered";
                    RegisterButton.IsEnabled = true;
                    break;
                case RegistrationState.Registering:
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                    StatusText.Text = "Registering...";
                    RegisterButton.IsEnabled = false;
                    break;
                case RegistrationState.Registered:
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                    StatusText.Text = "Registered";
                    RegisterButton.IsEnabled = true;
                    break;
                case RegistrationState.Failed:
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
                    StatusText.Text = "Failed";
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
                    CallDurationText.Text = "Duration: 00:00:00";
                    CallButton.IsEnabled = false;
                    HangupButton.IsEnabled = true;
                    _callTimer.Stop();
                    break;
                case CallState.Ringing:
                    CallStatusText.Text = "Ringing...";
                    break;
                case CallState.Connected:
                    CallStatusText.Text = "Connected";
                    _callTimer.Start();  // timer starts only when answered
                    break;
                case CallState.Ended:
                    _callTimer.Stop();
                    CallStatusText.Text = "Call ended";
                    CallDurationText.Text = "Duration: 00:00:00";
                    CallButton.IsEnabled = true;
                    HangupButton.IsEnabled = false;
                    break;
                case CallState.Failed:
                    _callTimer.Stop();
                    CallStatusText.Text = "Call failed";
                    CallDurationText.Text = "Duration: 00:00:00";
                    CallButton.IsEnabled = true;
                    HangupButton.IsEnabled = false;
                    break;
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _webRtcService?.Dispose();
        }
    }
}
