#nullable enable

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

        /// <summary>Mutable body without marker or length bytes, shared with the constructor's input array.</summary>
        public byte[] Payload { get; }
    }
}
