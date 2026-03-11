using System;

namespace WebRtcPhoneDialer.Core.Models
{
    /// <summary>
    /// Call quality tiers based on packet loss and jitter measurements.
    /// </summary>
    public enum NetworkCallQuality
    {
        Unknown,    // Not yet measured
        NoMedia,    // No RTP packets received
        Poor,       // Loss > 8% or jitter > 100 ms
        Fair,       // Loss > 3% or jitter > 50 ms
        Good,       // Loss > 1% or jitter > 20 ms
        Excellent   // Loss ≤ 1% and jitter ≤ 20 ms
    }

    /// <summary>
    /// Network quality snapshot produced every 5 seconds during an active call.
    /// </summary>
    public class NetworkQualityMetrics
    {
        /// <summary>Estimated packet loss percentage (0–100).</summary>
        public float PacketLossPct { get; set; }

        /// <summary>RFC 3550 inter-arrival jitter in milliseconds.</summary>
        public float JitterMs { get; set; }

        /// <summary>RTP packets received per second (last interval).</summary>
        public int RxPps { get; set; }

        /// <summary>RTP packets sent per second (last interval).</summary>
        public int TxPps { get; set; }

        /// <summary>Receive bitrate in kbps (last interval).</summary>
        public int RxKbps { get; set; }

        /// <summary>Transmit bitrate in kbps (last interval).</summary>
        public int TxKbps { get; set; }

        /// <summary>Overall quality tier.</summary>
        public NetworkCallQuality Quality { get; set; }

        /// <summary>True when at least one RTP packet has been received.</summary>
        public bool HasMedia { get; set; }

        /// <summary>Negotiated audio codec name (e.g. "PCMU", "PCMA").</summary>
        public string? Codec { get; set; }
    }
}
