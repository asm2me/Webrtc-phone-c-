using System;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using WebRtcPhoneDialer.Core.Models;
using WebRtcPhoneDialer.Core.Services;
using WebRtcPhoneDialer.ViewModels;
using WebRtcPhoneDialer.Windows;

namespace WebRtcPhoneDialer.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly WebRtcService _webRtcService;
        private readonly SettingsViewModel _viewModel;

        public SettingsWindow(AppSettings settings, WebRtcService webRtcService)
        {
            InitializeComponent();
            _webRtcService = webRtcService;
            _viewModel = new SettingsViewModel(settings);
            DataContext = _viewModel;

            // Populate PasswordBoxes (not data-bindable in WPF)
            WebRtcPasswordBox.Password = settings.Password;
            TurnPasswordBox.Password = settings.TurnPassword;
            AuthTokenBox.Password = settings.AuthToken;

            // Populate audio device lists from Windows drivers
            var audioDevices = new WindowsAudioDeviceProvider();
            var inputDevices = audioDevices.GetInputDevices();
            var outputDevices = audioDevices.GetOutputDevices();

            InputDeviceCombo.ItemsSource = inputDevices;
            OutputDeviceCombo.ItemsSource = outputDevices;
            RingDeviceCombo.ItemsSource = outputDevices;

            // Restore previously saved device selection by name
            InputDeviceCombo.SelectedItem = inputDevices.FirstOrDefault(d => d.Name == settings.InputDeviceId)
                                            ?? inputDevices.FirstOrDefault();
            OutputDeviceCombo.SelectedItem = outputDevices.FirstOrDefault(d => d.Name == settings.OutputDeviceId)
                                             ?? outputDevices.FirstOrDefault();
            RingDeviceCombo.SelectedItem = outputDevices.FirstOrDefault(d => d.Name == settings.RingDeviceId)
                                           ?? outputDevices.FirstOrDefault();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Password = WebRtcPasswordBox.Password;
            _viewModel.TurnPassword = TurnPasswordBox.Password;
            _viewModel.AuthToken = AuthTokenBox.Password;

            // Store selected device names
            if (InputDeviceCombo.SelectedItem is AudioDevice inputDev)
                _viewModel.InputDeviceId = inputDev.Name;
            if (OutputDeviceCombo.SelectedItem is AudioDevice outputDev)
                _viewModel.OutputDeviceId = outputDev.Name;
            if (RingDeviceCombo.SelectedItem is AudioDevice ringDev)
                _viewModel.RingDeviceId = ringDev.Name;

            var settings = _viewModel.ApplyToSettings();
            settings.Save();
            _webRtcService.Configure(settings);

            DialogResult = true;
            Close();
        }

        private void PreviewRingtone_Click(object sender, RoutedEventArgs e)
        {
            var selected = RingtoneCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(selected) || selected == "Default")
            {
                SystemSounds.Asterisk.Play();
                return;
            }

            var wavPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Media", selected);

            if (File.Exists(wavPath))
            {
                var player = new SoundPlayer(wavPath);
                player.Play();
            }
            else
            {
                SystemSounds.Asterisk.Play();
            }
        }

        private void TestOutputVolume_Click(object sender, RoutedEventArgs e)
        {
            SystemSounds.Beep.Play();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
