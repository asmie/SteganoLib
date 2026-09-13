using System;
using System.IO;

namespace SteganoLib.Jpeg
{
    /// <summary>
    /// Writes a <see cref="JpegImage"/> back to a baseline stream. Quantisation tables
    /// and metadata come from the image; Huffman tables are generated from the actual
    /// symbol statistics so any coefficient change stays encodable.
    /// </summary>
    internal sealed class JpegEncoder
    {
        private readonly JpegImage _image;
        private readonly int _mcusPerLine;
        private readonly int _mcusPerColumn;

        public JpegEncoder(JpegImage image)
        {
            _image = image;
            _mcusPerLine = CeilDiv(image.Width, 8 * image.MaxHorizontalSampling);
            _mcusPerColumn = CeilDiv(image.Height, 8 * image.MaxVerticalSampling);
        }

        public void Encode(Stream output)
        {
            var tables = BuildTables();

            output.WriteByte(JpegMarker.Prefix);
            output.WriteByte(JpegMarker.Soi);

            foreach (var segment in _image.Segments)
                WriteSegment(output, segment.Marker, segment.Payload);

            WriteQuantizationTables(output);
            WriteFrame(output);
            WriteHuffmanTables(output, tables);
            if (_image.RestartInterval > 0)
                WriteSegment(output, JpegMarker.Dri, new[] { (byte)(_image.RestartInterval >> 8), (byte)_image.RestartInterval });
            WriteScanHeader(output);

            var writer = new BitWriter(output);
            WriteScan(new HuffmanSink(writer, tables));
            writer.Flush();

            output.WriteByte(JpegMarker.Prefix);
            output.WriteByte(JpegMarker.Eoi);
        }

        private static int TableId(int componentIndex) => componentIndex == 0 ? 0 : 1;

        private (HuffmanTable[] Dc, HuffmanTable[] Ac) BuildTables()
        {
            var counter = new CountingSink(_image.Components.Count > 1 ? 2 : 1);
            WriteScan(counter);

            var dc = new HuffmanTable[counter.TableCount];
            var ac = new HuffmanTable[counter.TableCount];
            for (int i = 0; i < counter.TableCount; i++)
            {
                dc[i] = HuffmanTable.FromFrequencies(counter.DcFrequencies[i]);
                ac[i] = HuffmanTable.FromFrequencies(counter.AcFrequencies[i]);
            }
            return (dc, ac);
        }

        private void WriteScan(EntropySink sink)
        {
            var predictors = new int[_image.Components.Count];

            if (_image.Components.Count == 1)
            {
                var component = _image.Components[0];
                int blocksPerLine = CeilDiv(_image.Width, 8);
                int blocksPerColumn = CeilDiv(_image.Height, 8);
                int total = blocksPerLine * blocksPerColumn;
                for (int i = 0; i < total; i++)
                {
                    if (RestartDue(i, sink, predictors))
                        continue;
                    EncodeBlock(sink, 0, component.Block(i / blocksPerLine, i % blocksPerLine), ref predictors[0]);
                }
                return;
            }

            int mcus = _mcusPerLine * _mcusPerColumn;
            for (int mcu = 0; mcu < mcus; mcu++)
            {
                RestartDue(mcu, sink, predictors);
                int mcuRow = mcu / _mcusPerLine, mcuCol = mcu % _mcusPerLine;
                for (int c = 0; c < _image.Components.Count; c++)
                {
                    var component = _image.Components[c];
                    for (int v = 0; v < component.VerticalSampling; v++)
                    {
                        for (int h = 0; h < component.HorizontalSampling; h++)
                        {
                            var block = component.Block(mcuRow * component.VerticalSampling + v, mcuCol * component.HorizontalSampling + h);
                            EncodeBlock(sink, TableId(c), block, ref predictors[c]);
                        }
                    }
                }
            }
        }

        private bool RestartDue(int mcuIndex, EntropySink sink, int[] predictors)
        {
            if (_image.RestartInterval > 0 && mcuIndex > 0 && mcuIndex % _image.RestartInterval == 0)
            {
                sink.Restart((byte)(JpegMarker.Rst0 + ((mcuIndex / _image.RestartInterval - 1) & 7)));
                Array.Clear(predictors);
            }
            return false;
        }

        private static void EncodeBlock(EntropySink sink, int tableId, ReadOnlySpan<short> block, ref int predictor)
        {
            int diff = block[0] - predictor;
            predictor = block[0];
            int size = BitLength(Math.Abs(diff));
            sink.Dc(tableId, size);
            if (size > 0)
                sink.Bits(diff < 0 ? diff - 1 : diff, size);

            int run = 0;
            for (int k = 1; k < 64; k++)
            {
                int value = block[k];
                if (value == 0)
                {
                    run++;
                    continue;
                }

                while (run > 15)
                {
                    sink.Ac(tableId, 0xF0);
                    run -= 16;
                }

                int s = BitLength(Math.Abs(value));
                sink.Ac(tableId, (run << 4) | s);
                sink.Bits(value < 0 ? value - 1 : value, s);
                run = 0;
            }

            if (run > 0)
                sink.Ac(tableId, 0x00);
        }

        private static int BitLength(int value)
        {
            int bits = 0;
            while (value != 0)
            {
                bits++;
                value >>= 1;
            }
            return bits;
        }

        private void WriteQuantizationTables(Stream output)
        {
            for (int id = 0; id < _image.QuantizationTables.Length; id++)
            {
                var table = _image.QuantizationTables[id];
                if (table == null)
                    continue;

                bool wide = false;
                foreach (var q in table) wide |= q > 255;

                var body = new byte[1 + (wide ? 128 : 64)];
                body[0] = (byte)((wide ? 0x10 : 0x00) | id);
                for (int i = 0; i < 64; i++)
                {
                    if (wide)
                    {
                        body[1 + 2 * i] = (byte)(table[i] >> 8);
                        body[2 + 2 * i] = (byte)table[i];
                    }
                    else
                    {
                        body[1 + i] = (byte)table[i];
                    }
                }
                WriteSegment(output, JpegMarker.Dqt, body);
            }
        }

        private void WriteFrame(Stream output)
        {
            var body = new byte[6 + 3 * _image.Components.Count];
            body[0] = 8;
            body[1] = (byte)(_image.Height >> 8);
            body[2] = (byte)_image.Height;
            body[3] = (byte)(_image.Width >> 8);
            body[4] = (byte)_image.Width;
            body[5] = (byte)_image.Components.Count;
            for (int i = 0; i < _image.Components.Count; i++)
            {
                var c = _image.Components[i];
                body[6 + 3 * i] = c.Id;
                body[7 + 3 * i] = (byte)((c.HorizontalSampling << 4) | c.VerticalSampling);
                body[8 + 3 * i] = (byte)c.QuantizationTableId;
            }
            WriteSegment(output, _image.FrameMarker, body);
        }

        private static void WriteHuffmanTables(Stream output, (HuffmanTable[] Dc, HuffmanTable[] Ac) tables)
        {
            for (int id = 0; id < tables.Dc.Length; id++)
            {
                WriteSegment(output, JpegMarker.Dht, TableBody(0, id, tables.Dc[id]));
                WriteSegment(output, JpegMarker.Dht, TableBody(1, id, tables.Ac[id]));
            }
        }

        private static byte[] TableBody(int tableClass, int id, HuffmanTable table)
        {
            var body = new byte[17 + table.Symbols.Length];
            body[0] = (byte)((tableClass << 4) | id);
            table.Counts.CopyTo(body, 1);
            table.Symbols.CopyTo(body, 17);
            return body;
        }

        private void WriteScanHeader(Stream output)
        {
            var body = new byte[4 + 2 * _image.Components.Count];
            body[0] = (byte)_image.Components.Count;
            for (int i = 0; i < _image.Components.Count; i++)
            {
                body[1 + 2 * i] = _image.Components[i].Id;
                body[2 + 2 * i] = (byte)((TableId(i) << 4) | TableId(i));
            }
            body[1 + 2 * _image.Components.Count] = 0;
            body[2 + 2 * _image.Components.Count] = 63;
            body[3 + 2 * _image.Components.Count] = 0;
            WriteSegment(output, JpegMarker.Sos, body);
        }

        private static void WriteSegment(Stream output, byte marker, byte[] body)
        {
            int length = body.Length + 2;
            if (length > 0xFFFF)
                throw new InvalidDataException("Segment too long.");

            output.WriteByte(JpegMarker.Prefix);
            output.WriteByte(marker);
            output.WriteByte((byte)(length >> 8));
            output.WriteByte((byte)length);
            output.Write(body, 0, body.Length);
        }

        private static int CeilDiv(int a, int b) => (a + b - 1) / b;

        private abstract class EntropySink
        {
            public abstract void Dc(int tableId, int symbol);
            public abstract void Ac(int tableId, int symbol);
            public abstract void Bits(int value, int count);
            public abstract void Restart(byte marker);
        }

        private sealed class CountingSink : EntropySink
        {
            public CountingSink(int tableCount)
            {
                TableCount = tableCount;
                DcFrequencies = new long[tableCount][];
                AcFrequencies = new long[tableCount][];
                for (int i = 0; i < tableCount; i++)
                {
                    DcFrequencies[i] = new long[256];
                    AcFrequencies[i] = new long[256];
                }
            }

            public int TableCount { get; }
            public long[][] DcFrequencies { get; }
            public long[][] AcFrequencies { get; }

            public override void Dc(int tableId, int symbol) => DcFrequencies[tableId][symbol]++;
            public override void Ac(int tableId, int symbol) => AcFrequencies[tableId][symbol]++;
            public override void Bits(int value, int count) { }
            public override void Restart(byte marker) { }
        }

        private sealed class HuffmanSink : EntropySink
        {
            private readonly BitWriter _writer;
            private readonly (HuffmanTable[] Dc, HuffmanTable[] Ac) _tables;

            public HuffmanSink(BitWriter writer, (HuffmanTable[] Dc, HuffmanTable[] Ac) tables)
            {
                _writer = writer;
                _tables = tables;
            }

            public override void Dc(int tableId, int symbol) => _tables.Dc[tableId].Encode(_writer, symbol);
            public override void Ac(int tableId, int symbol) => _tables.Ac[tableId].Encode(_writer, symbol);
            public override void Bits(int value, int count) => _writer.WriteBits(value & ((1 << count) - 1), count);
            public override void Restart(byte marker) => _writer.WriteMarker(marker);
        }
    }
}
