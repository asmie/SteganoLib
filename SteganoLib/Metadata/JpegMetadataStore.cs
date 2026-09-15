#nullable enable

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
    /// 65533 bytes, so larger payloads span several segments. Supports progressive JPEG
    /// without decoding pixels: marker framing is validated and scan bytes are preserved.
    /// Only matching segments before the first scan are used for payload entries.
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
        private readonly IReadOnlyList<JpegSegment> _segmentView;
        private readonly Dictionary<JpegSegment, int> _prefixCounts = new();
        private readonly byte[] _tail;
        private readonly byte[] _prefix;

        /// <exception cref="InvalidDataException">Invalid JPEG marker framing or truncated segments, scans or end marker.</exception>
        public JpegMetadataStore(byte[] jpeg, int appNumber = DefaultAppNumber, string identifier = DefaultIdentifier)
        {
            if (jpeg == null) throw new ArgumentNullException(nameof(jpeg));
            if (appNumber < 0 || appNumber > 15) throw new ArgumentOutOfRangeException(nameof(appNumber), "APP marker number must be 0 to 15.");

            AppNumber = appNumber;
            Identifier = ValidateIdentifier(identifier);
            _prefix = Identifier.Length == 0 ? Array.Empty<byte>() : Encoding.ASCII.GetBytes(Identifier + "\0");
            _tail = Parse(jpeg);
            _segmentView = _segments.AsReadOnly();
        }

        /// <summary>APPn marker number, 0 to 15.</summary>
        public int AppNumber { get; }

        /// <summary>ASCII identifier written before each entry, followed by a NUL byte. Empty for none.</summary>
        public string Identifier { get; }

        /// <summary>Marker byte of the entry segments.</summary>
        public byte Marker => (byte)(JpegMarker.App0 + AppNumber);

        /// <summary>Live read-only collection of header segments before the first scan. Payload arrays remain editable; callers must preserve valid JPEG structure.</summary>
        public IReadOnlyList<JpegSegment> Segments => _segmentView;

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

            _segments.RemoveAll(segment =>
            {
                if (!IsMine(segment))
                    return false;
                _prefixCounts.Remove(segment);
                return true;
            });

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
                int prefixCount = _prefixCounts.TryGetValue(segment, out int count) ? count : 1;
                for (int i = 0; i < prefixCount; i++)
                    stream.WriteByte(JpegMarker.Prefix);
                stream.WriteByte(segment.Marker);
                if (segment.Marker == 0x01) // TEM has no length or payload.
                    continue;
                stream.WriteByte((byte)(length >> 8));
                stream.WriteByte((byte)length);
                stream.Write(segment.Payload);
            }
            stream.Write(_tail);
            return stream.ToArray();
        }

        /// <summary>Checks marker framing throughout the file, retaining scan data without decoding it.</summary>
        private byte[] Parse(byte[] jpeg)
        {
            if (!IsJpeg(jpeg))
                throw new InvalidDataException("Not a JPEG file.");

            int pos = 2;
            int tailStart = -1;
            bool inScan = false, hasFrame = false;
            while (true)
            {
                if (inScan)
                {
                    while (pos < jpeg.Length && jpeg[pos] != JpegMarker.Prefix)
                        pos++;
                }
                int start = pos;
                if (pos >= jpeg.Length || jpeg[pos] != JpegMarker.Prefix)
                    throw new InvalidDataException("Expected a JPEG marker.");
                while (pos < jpeg.Length && jpeg[pos] == JpegMarker.Prefix)
                    pos++; // fill bytes
                if (pos >= jpeg.Length)
                    throw new InvalidDataException("Truncated JPEG marker.");

                int prefixCount = pos - start;
                byte marker = jpeg[pos++];
                if (inScan && (marker == 0 || JpegMarker.IsRestart(marker) || marker == 0x01))
                {
                    if (marker == 0 && prefixCount != 1)
                        throw new InvalidDataException("Invalid JPEG byte stuffing after marker fill bytes.");
                    continue;
                }
                if (marker == JpegMarker.Eoi)
                {
                    if (tailStart < 0 || pos != jpeg.Length)
                        throw new InvalidDataException("JPEG must end with EOI after a scan, without trailing bytes.");
                    return jpeg.AsSpan(tailStart).ToArray();
                }
                if (marker == JpegMarker.Soi || JpegMarker.IsRestart(marker) || (marker < 0xC0 && marker != 0x01))
                    throw new InvalidDataException("Unexpected JPEG marker outside scan data.");
                if (marker == 0x01)
                {
                    if (tailStart < 0)
                        AddHeaderSegment(marker, Array.Empty<byte>(), prefixCount);
                    continue;
                }

                if (jpeg.Length - pos < 2)
                    throw new InvalidDataException("Truncated JPEG segment.");
                int length = (jpeg[pos] << 8) | jpeg[pos + 1];
                if (length < 2 || length > jpeg.Length - pos)
                    throw new InvalidDataException("Corrupt JPEG segment length.");

                var body = jpeg.AsSpan(pos + 2, length - 2);
                if (IsFrame(marker))
                {
                    if (body.Length < 6 || body[5] == 0 || body.Length != 6 + 3 * body[5] ||
                        (body[3] == 0 && body[4] == 0))
                        throw new InvalidDataException("Invalid JPEG frame header length or dimensions.");
                    hasFrame = true;
                }
                if (marker == JpegMarker.Sos)
                {
                    if (!hasFrame || body.Length < 4 || body[0] is < 1 or > 4 || body.Length != 4 + 2 * body[0])
                        throw new InvalidDataException("Invalid JPEG scan header or missing frame.");
                    if (tailStart < 0)
                        tailStart = start;
                    inScan = true;
                }
                else if (marker == 0xDC) // DNL may interrupt entropy data, which resumes after it.
                {
                    if (!inScan || body.Length != 2 || (body[0] == 0 && body[1] == 0))
                        throw new InvalidDataException("Invalid JPEG DNL segment.");
                }
                else
                {
                    inScan = false;
                    if (tailStart < 0)
                        AddHeaderSegment(marker, body.ToArray(), prefixCount);
                }
                pos += length;
            }
        }

        private void AddHeaderSegment(byte marker, byte[] body, int prefixCount)
        {
            var segment = new JpegSegment(marker, body);
            _segments.Add(segment);
            if (prefixCount > 1)
                _prefixCounts.Add(segment, prefixCount);
        }

        private static bool IsFrame(byte marker) => marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);

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
