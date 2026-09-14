using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using SteganoLib.Jpeg;

namespace SteganoLib.Metadata
{
    /// <summary>
    /// Stores payload entries as JPEG APPn segments. Each entry becomes one segment with
    /// marker APP<see cref="AppNumber"/> whose body starts with <see cref="Identifier"/>
    /// and a NUL byte, the way JFIF, Exif and ICC segments announce themselves. Entries
    /// are inserted after the last existing APPn segment; a segment body is limited to
    /// 65533 bytes, so larger payloads span several segments. Works on any JPEG,
    /// progressive included, because the scan data is copied verbatim.
    /// Survives copying and metadata-preserving edits. Destroyed by re-encoding or by
    /// tools that strip unknown application segments.
    /// </summary>
    public sealed class JpegMetadataStore : IMetadataStore
    {
        public const int DefaultAppNumber = 9;
        public const string DefaultIdentifier = "META";
        public const int MaxIdentifierLength = 64;

        private const int MaxSegmentBody = 0xFFFF - 2;

        private readonly List<JpegSegment> _segments = new();
        private readonly byte[] _tail;
        private readonly byte[] _prefix;

        /// <exception cref="InvalidDataException">Not a JPEG or the header is corrupt.</exception>
        public JpegMetadataStore(byte[] jpeg, int appNumber = DefaultAppNumber, string identifier = DefaultIdentifier)
        {
            if (jpeg == null) throw new ArgumentNullException(nameof(jpeg));
            if (appNumber < 0 || appNumber > 15) throw new ArgumentOutOfRangeException(nameof(appNumber), "APP marker number must be 0 to 15.");

            AppNumber = appNumber;
            Identifier = ValidateIdentifier(identifier);
            _prefix = Identifier.Length == 0 ? Array.Empty<byte>() : Encoding.ASCII.GetBytes(Identifier + "\0");
            _tail = Parse(jpeg);
        }

        /// <summary>APPn marker number, 0 to 15.</summary>
        public int AppNumber { get; }

        /// <summary>ASCII identifier written before each entry, followed by a NUL byte. Empty for none.</summary>
        public string Identifier { get; }

        /// <summary>Marker byte of the entry segments.</summary>
        public byte Marker => (byte)(JpegMarker.App0 + AppNumber);

        /// <summary>Header segments before the first scan, in file order, entry segments included.</summary>
        public IReadOnlyList<JpegSegment> Segments => _segments;

        public int MaxEntrySize => MaxSegmentBody - _prefix.Length;

        public long MaxTotalSize => long.MaxValue;

        public static bool IsJpeg(ReadOnlySpan<byte> data)
        {
            return data.Length >= 3 && data[0] == JpegMarker.Prefix && data[1] == JpegMarker.Soi && data[2] == JpegMarker.Prefix;
        }

        public IReadOnlyList<byte[]> ReadEntries()
        {
            var entries = new List<byte[]>();
            foreach (var segment in _segments)
            {
                if (IsMine(segment))
                    entries.Add(segment.Payload.AsSpan(_prefix.Length).ToArray());
            }
            return entries;
        }

        public void WriteEntries(IReadOnlyList<byte[]> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            var encoded = new List<JpegSegment>(entries.Count);
            foreach (var entry in entries)
            {
                if (entry == null)
                    throw new ArgumentException("Entries must not be null.", nameof(entries));
                if (entry.Length > MaxEntrySize)
                    throw new ArgumentException($"Entry of {entry.Length} bytes exceeds the segment limit of {MaxEntrySize}.", nameof(entries));

                var body = new byte[_prefix.Length + entry.Length];
                _prefix.CopyTo(body, 0);
                entry.CopyTo(body, _prefix.Length);
                encoded.Add(new JpegSegment(Marker, body));
            }

            _segments.RemoveAll(IsMine);

            int insertAt = 0;
            for (int i = 0; i < _segments.Count; i++)
            {
                if (JpegMarker.IsApp(_segments[i].Marker))
                    insertAt = i + 1;
            }
            _segments.InsertRange(insertAt, encoded);
        }

        public byte[] ToArray()
        {
            using var stream = new MemoryStream();
            stream.WriteByte(JpegMarker.Prefix);
            stream.WriteByte(JpegMarker.Soi);
            foreach (var segment in _segments)
            {
                int length = segment.Payload.Length + 2;
                stream.WriteByte(JpegMarker.Prefix);
                stream.WriteByte(segment.Marker);
                stream.WriteByte((byte)(length >> 8));
                stream.WriteByte((byte)length);
                stream.Write(segment.Payload);
            }
            stream.Write(_tail);
            return stream.ToArray();
        }

        /// <summary>Reads the header segments into the segment list and returns everything from the first scan on.</summary>
        private byte[] Parse(byte[] jpeg)
        {
            if (!IsJpeg(jpeg))
                throw new InvalidDataException("Not a JPEG file.");

            int pos = 2;
            while (true)
            {
                int start = pos;
                if (pos >= jpeg.Length || jpeg[pos] != JpegMarker.Prefix)
                    throw new InvalidDataException("Expected a JPEG marker.");
                while (pos < jpeg.Length && jpeg[pos] == JpegMarker.Prefix)
                    pos++; // fill bytes
                if (pos >= jpeg.Length)
                    throw new InvalidDataException("JPEG has no scan.");

                byte marker = jpeg[pos++];
                if (marker == JpegMarker.Sos || marker == JpegMarker.Eoi || IsStandalone(marker))
                    return jpeg.AsSpan(start).ToArray();

                if (pos + 2 > jpeg.Length)
                    throw new InvalidDataException("Truncated JPEG segment.");
                int length = (jpeg[pos] << 8) | jpeg[pos + 1];
                if (length < 2 || pos + length > jpeg.Length)
                    throw new InvalidDataException("Corrupt JPEG segment length.");

                _segments.Add(new JpegSegment(marker, jpeg.AsSpan(pos + 2, length - 2).ToArray()));
                pos += length;
            }
        }

        private static bool IsStandalone(byte marker)
        {
            return marker == 0x01 || JpegMarker.IsRestart(marker) || marker == JpegMarker.Soi;
        }

        private bool IsMine(JpegSegment segment)
        {
            return segment.Marker == Marker && segment.Payload.AsSpan().StartsWith(_prefix);
        }

        private static string ValidateIdentifier(string identifier)
        {
            if (identifier == null)
                throw new ArgumentNullException(nameof(identifier));
            if (identifier.Length > MaxIdentifierLength)
                throw new ArgumentException($"Identifier must be at most {MaxIdentifierLength} characters.", nameof(identifier));
            foreach (char c in identifier)
            {
                if (c < 0x20 || c > 0x7E)
                    throw new ArgumentException("Identifier must be printable ASCII.", nameof(identifier));
            }
            return identifier;
        }
    }
}
