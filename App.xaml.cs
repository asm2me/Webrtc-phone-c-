using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebRtcPhoneDialer.Core.Models;

namespace WebRtcPhoneDialer
{
    public partial class App : Application
    {
        private const string MutexName   = "WebRtcPhoneDialer_SingleInstance";
        private const string PipeName    = "WebRtcPhoneDialer_Provision";

        /// <summary>Set when a voipat://provision URL was applied on startup.</summary>
        public static string? ProvisionedExtension { get; private set; }
        public static string? ProvisionedDisplay   { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // ── 1. Extract voipat://provision?t=... from command-line args ────────
            string? provisionUrl = null;
            foreach (var arg in e.Args)
            {
                if (arg.StartsWith("voipat://", StringComparison.OrdinalIgnoreCase))
                {
                    provisionUrl = arg;
                    break;
                }
            }

            // ── 2. Single-instance guard ──────────────────────────────────────────
            var mutex = new Mutex(true, MutexName, out bool isFirstInstance);

            if (!isFirstInstance)
            {
                // Another instance is running — forward the URL via named pipe and exit
                if (provisionUrl != null)
                    SendProvisionUrlToRunningInstance(provisionUrl);
                Shutdown();
                return;
            }

            GC.KeepAlive(mutex);

            // ── 3. If launched via provisioning URL, decode and apply settings ────
            if (provisionUrl != null)
                ApplyProvisionUrl(provisionUrl);

            // ── 4. Start a named-pipe listener so a second launch can forward URLs
            StartPipeListener();

            base.OnStartup(e);
        }

        // ─────────────────────────────────────────────────────────────────────────

        private static void ApplyProvisionUrl(string url)
        {
            try
            {
                // Parse: voipat://provision?t=<jwt>
                var uri   = new Uri(url);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var token = query["t"];
                if (string.IsNullOrEmpty(token)) return;

                var cfg = DecodeJwtPayload(token);
                if (cfg == null || (string?)cfg["type"] != "provision") return;

                // Check expiry
                var exp = (long?)cfg["exp"] ?? 0;
                if (exp > 0 && DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp)
                {
                    MessageBox.Show(
                        "This provisioning link has expired.\nAsk your administrator to generate a new one.",
                        "Link Expired", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Build and save settings
                var existing = AppSettings.Load();
                existing.Username         = (string?)cfg["ext"]    ?? existing.Username;
                existing.Password         = (string?)cfg["pass"]   ?? existing.Password;
                existing.SipDomain        = (string?)cfg["domain"] ?? existing.SipDomain;
                existing.SignalingServerUrl= (string?)cfg["wss"]   ?? existing.SignalingServerUrl;
                existing.StunServer       = (string?)cfg["stun"]  ?? existing.StunServer;

                var codec = (string?)cfg["codec"];
                if (!string.IsNullOrEmpty(codec))
                    existing.AudioCodecName = codec;

                existing.Save();

                ProvisionedExtension = (string?)cfg["ext"];
                ProvisionedDisplay   = (string?)cfg["display"] ?? ProvisionedExtension;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not apply provisioning link:\n{ex.Message}",
                    "Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static JObject? DecodeJwtPayload(string token)
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;

            var b64 = parts[1].Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4)
            {
                case 2: b64 += "=="; break;
                case 3: b64 += "=";  break;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            return JObject.Parse(json);
        }

        private static void SendProvisionUrlToRunningInstance(string url)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(2000);
                var bytes = Encoding.UTF8.GetBytes(url);
                client.Write(bytes, 0, bytes.Length);
            }
            catch { /* running instance may be in a state where it can't accept */ }
        }

        private void StartPipeListener()
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                        server.WaitForConnection();
                        var buf = new byte[8192];
                        int read = server.Read(buf, 0, buf.Length);
                        var url  = Encoding.UTF8.GetString(buf, 0, read);
                        if (!string.IsNullOrEmpty(url))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                ApplyProvisionUrl(url);
                                if (ProvisionedDisplay != null)
                                    ShowProvisionedToast(ProvisionedDisplay, ProvisionedExtension!);
                            });
                        }
                    }
                    catch { break; }
                }
            })
            { IsBackground = true, Name = "ProvisionPipeListener" };
            thread.Start();
        }

        internal static void ShowProvisionedToast(string display, string ext)
        {
            MessageBox.Show(
                $"Your phone has been configured for:\n\n" +
                $"  Name:      {display}\n" +
                $"  Extension: {ext}\n\n" +
                "Settings saved. Restart the app to connect.",
                "Phone Configured", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
