using System;

namespace SteganoLib.Jpeg
{
    /// <summary>A marker segment kept verbatim (APPn, COM) so metadata survives re-encoding.</summary>
    public sealed class JpegSegment
    {
        public JpegSegment(byte marker, byte[] payload)
        {
            Marker = marker;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public byte Marker { get; }

        /// <summary>Segment body without the marker and the two length bytes.</summary>
        public byte[] Payload { get; }
    }
}
