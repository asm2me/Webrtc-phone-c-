using System;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WebRtcPhoneDialer.Core.Models;

namespace WebRtcPhoneDialer.Views
{
    public partial class IncomingCallWindow : Window
    {
        private SoundPlayer? _ringtonePlayer;
        private DispatcherTimer? _ringtoneLoopTimer;
        private bool _isMuted;

        /// <summary>True if the user clicked Answer.</summary>
        public bool Answered { get; private set; }

        public IncomingCallWindow(string callerInfo, AppSettings settings)
        {
            InitializeComponent();
            CallerIdText.Text = callerInfo;
            StartPulseAnimation();
            StartRingtone(settings);
        }

        // ── Ringtone ─────────────────────────────────────────────────────────────

        private void StartRingtone(AppSettings settings)
        {
            try
            {
                string? wavPath = null;

                if (!string.IsNullOrEmpty(settings.RingtoneName) && settings.RingtoneName != "Default")
                {
                    var candidate = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                        "Media", settings.RingtoneName);
                    if (File.Exists(candidate))
                        wavPath = candidate;
                }

                if (wavPath != null)
                {
                    _ringtonePlayer = new SoundPlayer(wavPath);
                    _ringtonePlayer.Load();
                    _ringtonePlayer.Play();

                    // Loop the ringtone every few seconds
                    _ringtoneLoopTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                    _ringtoneLoopTimer.Tick += (_, _) =>
                    {
                        if (!_isMuted)
                        {
                            try { _ringtonePlayer?.Play(); } catch { }
                        }
                    };
                    _ringtoneLoopTimer.Start();
                }
                else
                {
                    // Default system sound, looped
                    _ringtoneLoopTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    _ringtoneLoopTimer.Tick += (_, _) =>
                    {
                        if (!_isMuted)
                        {
                            try { SystemSounds.Asterisk.Play(); } catch { }
                        }
                    };
                    SystemSounds.Asterisk.Play();
                    _ringtoneLoopTimer.Start();
                }
            }
            catch
            {
                // Fallback — at least beep
                try { SystemSounds.Asterisk.Play(); } catch { }
            }
        }

        private void StopRingtone()
        {
            _ringtoneLoopTimer?.Stop();
            _ringtoneLoopTimer = null;

            try { _ringtonePlayer?.Stop(); } catch { }
            _ringtonePlayer?.Dispose();
            _ringtonePlayer = null;
        }

        // ── Pulse animation ──────────────────────────────────────────────────────

        private void StartPulseAnimation()
        {
            var scaleX = new DoubleAnimation(1.0, 1.5, TimeSpan.FromSeconds(1.2))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            var scaleY = new DoubleAnimation(1.0, 1.5, TimeSpan.FromSeconds(1.2))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            var opacity = new DoubleAnimation(0.7, 0.0, TimeSpan.FromSeconds(1.2))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };

            var transform = new System.Windows.Media.ScaleTransform(1, 1);
            PulseRing.RenderTransform = transform;
            PulseRing.RenderTransformOrigin = new Point(0.5, 0.5);

            transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleX);
            transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleY);
            PulseRing.BeginAnimation(OpacityProperty, opacity);
        }

        // ── Button handlers ──────────────────────────────────────────────────────

        private void Answer_Click(object sender, RoutedEventArgs e)
        {
            Answered = true;
            StopRingtone();
            DialogResult = true;
        }

        private void Reject_Click(object sender, RoutedEventArgs e)
        {
            Answered = false;
            StopRingtone();
            DialogResult = false;
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            _isMuted = !_isMuted;
            if (_isMuted)
            {
                try { _ringtonePlayer?.Stop(); } catch { }
                MuteIcon.Text = "\U0001F507"; // muted speaker
                MuteLabel.Text = "Unmute";
            }
            else
            {
                try { _ringtonePlayer?.Play(); } catch { }
                MuteIcon.Text = "\U0001F50A"; // speaker
                MuteLabel.Text = "Mute";
            }
        }

        // ── Keyboard ─────────────────────────────────────────────────────────────

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Answer_Click(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Reject_Click(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.M)
            {
                e.Handled = true;
                Mute_Click(this, new RoutedEventArgs());
            }
        }

        // ── Cleanup ──────────────────────────────────────────────────────────────

        protected override void OnClosed(EventArgs e)
        {
            StopRingtone();
            base.OnClosed(e);
        }
    }
}
