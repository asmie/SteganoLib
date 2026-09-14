using System;
using System.Collections.Generic;
using System.IO;

namespace SteganoLib.Jpeg
{
    /// <summary>
    /// An 8-bit baseline or extended-sequential JPEG opened at the coefficient level. Quantised DCT coefficients can
    /// be changed and the file written back without a second lossy compression;
    /// quantisation tables and metadata segments are preserved and Huffman tables are
    /// rebuilt for the new coefficient statistics.
    /// </summary>
    public sealed class JpegImage
    {
        internal JpegImage(int width, int height, byte frameMarker, List<JpegComponent> components, ushort[][] quantizationTables, int restartInterval, List<JpegSegment> segments)
        {
            Width = width;
            Height = height;
            FrameMarker = frameMarker;
            Components = components;
            QuantizationTables = quantizationTables;
            RestartInterval = restartInterval;
            Segments = segments;
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>SOF marker of the source file (0xC0 baseline or 0xC1 extended sequential).</summary>
        public byte FrameMarker { get; }

        public IReadOnlyList<JpegComponent> Components { get; }

        /// <summary>Up to four tables of 64 entries in zigzag order; unused slots are <c>null</c>.</summary>
        public ushort[][] QuantizationTables { get; }

        /// <summary>MCUs between restart markers, 0 for none. Applied on save.</summary>
        public int RestartInterval
        {
            get => _restartInterval;
            set
            {
                if (value < 0 || value > 0xFFFF)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _restartInterval = value;
            }
        }

        private int _restartInterval;

        /// <summary>APPn and COM segments in file order, written back verbatim.</summary>
        public IReadOnlyList<JpegSegment> Segments { get; }

        /// <summary>Highest horizontal sampling factor across components.</summary>
        public int MaxHorizontalSampling
        {
            get
            {
                int max = 1;
                foreach (var c in Components) max = Math.Max(max, c.HorizontalSampling);
                return max;
            }
        }

        /// <summary>Highest vertical sampling factor across components.</summary>
        public int MaxVerticalSampling
        {
            get
            {
                int max = 1;
                foreach (var c in Components) max = Math.Max(max, c.VerticalSampling);
                return max;
            }
        }

        /// <exception cref="NotSupportedException">Unsupported JPEG processes, deferred height or quantisation-table redefinition after use.</exception>
        /// <exception cref="InvalidDataException">Invalid structure, tables, scan data or padding. Truncated scans are not repaired.</exception>
        public static JpegImage Load(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return Load(File.ReadAllBytes(path));
        }

        public static JpegImage Load(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return Load(buffer.ToArray());
        }

        public static JpegImage Load(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return new JpegDecoder(data).Decode();
        }

        public void Save(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            File.WriteAllBytes(path, ToArray());
        }

        public void Save(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var bytes = ToArray();
            stream.Write(bytes, 0, bytes.Length);
        }

        public byte[] ToArray()
        {
            using var output = new MemoryStream();
            new JpegEncoder(this).Encode(output);
            return output.ToArray();
        }

        public JpegImage Clone()
        {
            var components = new List<JpegComponent>(Components.Count);
            foreach (var c in Components)
                components.Add(c.Clone());

            var tables = new ushort[QuantizationTables.Length][];
            for (int i = 0; i < tables.Length; i++)
                tables[i] = (ushort[])QuantizationTables[i]?.Clone();

            return new JpegImage(Width, Height, FrameMarker, components, tables, RestartInterval, new List<JpegSegment>(Segments));
        }
    }
}
