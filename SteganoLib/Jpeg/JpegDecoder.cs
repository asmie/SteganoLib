using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace SteganoLib.Jpeg
{
    /// <summary>Parses a baseline or extended-sequential Huffman JPEG down to its quantised coefficients.</summary>
    internal sealed class JpegDecoder
    {
        private readonly byte[] _data;
        private int _pos;

        private int _width;
        private int _height;
        private byte _frameMarker;
        private readonly List<JpegComponent> _components = new();
        private readonly ushort[][] _quantTables = new ushort[4][];
        private readonly HuffmanTable[] _dcTables = new HuffmanTable[4];
        private readonly HuffmanTable[] _acTables = new HuffmanTable[4];
        private readonly List<JpegSegment> _segments = new();
        private int _restartInterval;
        private bool _frameSeen;
        private readonly HashSet<byte> _scannedComponents = new();

        public JpegDecoder(byte[] data)
        {
            _data = data;
        }

        public JpegImage Decode()
        {
            if (_data.Length < 4 || _data[0] != JpegMarker.Prefix || _data[1] != JpegMarker.Soi)
                throw new InvalidDataException("Not a JPEG file.");
            _pos = 2;

            while (true)
            {
                byte marker = ReadMarker();
                switch (marker)
                {
                    case JpegMarker.Eoi:
                        if (!_frameSeen || _scannedComponents.Count != _components.Count)
                            throw new InvalidDataException("JPEG is missing scan data for one or more components.");
                        return new JpegImage(_width, _height, _frameMarker, _components, _quantTables, _restartInterval, _segments);

                    case JpegMarker.Sof0:
                    case JpegMarker.Sof1:
                        ReadFrame(marker);
                        break;

                    case JpegMarker.Dht:
                        ReadHuffmanTables();
                        break;

                    case JpegMarker.Dqt:
                        ReadQuantizationTables();
                        break;

                    case JpegMarker.Dri:
                        ReadRestartInterval();
                        break;

                    case JpegMarker.Sos:
                        ReadScan();
                        break;

                    case JpegMarker.Com:
                        _segments.Add(new JpegSegment(marker, ReadSegmentBody().ToArray()));
                        break;

                    default:
                        if (JpegMarker.IsApp(marker))
                        {
                            _segments.Add(new JpegSegment(marker, ReadSegmentBody().ToArray()));
                        }
                        else if (JpegMarker.IsSof(marker))
                        {
                            throw new NotSupportedException(marker == JpegMarker.Sof2
                                ? "Progressive JPEG is not supported."
                                : $"JPEG process 0x{marker:X2} is not supported; only baseline Huffman is.");
                        }
                        else if (JpegMarker.IsRestart(marker) || marker == JpegMarker.Soi || marker == JpegMarker.Stuffing)
                        {
                            throw new InvalidDataException("Unexpected standalone JPEG marker.");
                        }
                        else
                        {
                            ReadSegmentBody();
                        }
                        break;
                }
            }
        }

        private byte ReadMarker()
        {
            if (_pos >= _data.Length || _data[_pos] != JpegMarker.Prefix)
                throw new InvalidDataException("Expected a JPEG marker.");
            while (_pos < _data.Length && _data[_pos] == JpegMarker.Prefix)
                _pos++;
            if (_pos >= _data.Length)
                throw new InvalidDataException("Unexpected end of JPEG data.");

            return _data[_pos++];
        }

        private ReadOnlySpan<byte> ReadSegmentBody()
        {
            if (_data.Length - _pos < 2)
                throw new InvalidDataException("Missing segment length.");
            int length = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(_pos));
            if (length < 2 || length > _data.Length - _pos)
                throw new InvalidDataException("Corrupt segment length.");

            var body = _data.AsSpan(_pos + 2, length - 2);
            _pos += length;
            return body;
        }

        private void ReadFrame(byte marker)
        {
            if (_frameSeen)
                throw new InvalidDataException("Multiple frame headers.");
            _frameSeen = true;
            _frameMarker = marker;

            var body = ReadSegmentBody();
            if (body.Length < 6)
                throw new InvalidDataException("Corrupt frame header.");
            int count = body[5];
            if (count < 1 || count > 4 || body.Length != 6 + 3 * count)
                throw new InvalidDataException("Corrupt frame header.");
            int precision = body[0];
            if (precision != 8)
            {
                if (precision == 12)
                    throw new NotSupportedException("12-bit JPEG is not supported.");
                throw new InvalidDataException("Invalid sequential JPEG sample precision.");
            }

            _height = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(1));
            _width = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(3));
            if (_width == 0)
                throw new InvalidDataException("JPEG width must be positive.");
            if (_height == 0)
                throw new NotSupportedException("JPEG with deferred height (DNL) is not supported.");

            var raw = new (byte Id, int H, int V, int Tq)[count];
            var ids = new HashSet<byte>();
            int hMax = 1, vMax = 1;
            for (int i = 0; i < count; i++)
            {
                byte id = body[6 + 3 * i];
                byte hv = body[7 + 3 * i];
                byte tq = body[8 + 3 * i];
                int h = hv >> 4, v = hv & 15;
                if (!ids.Add(id) || h < 1 || h > 4 || v < 1 || v > 4 || tq > 3)
                    throw new InvalidDataException("Corrupt component descriptor.");
                raw[i] = (id, h, v, tq);
                hMax = Math.Max(hMax, h);
                vMax = Math.Max(vMax, v);
            }

            int mcusPerLine = CeilDiv(_width, 8 * hMax);
            int mcusPerColumn = CeilDiv(_height, 8 * vMax);
            long minimumBlocks = 0;
            foreach (var (_, h, v, _) in raw)
            {
                long coefficients = (long)mcusPerLine * h * mcusPerColumn * v * JpegComponent.BlockSize;
                if (coefficients > Array.MaxLength)
                    throw new InvalidDataException("JPEG component exceeds the supported array length.");
                minimumBlocks += (long)CeilDiv(CeilDiv(_width * h, hMax), 8)
                    * CeilDiv(CeilDiv(_height * v, vMax), 8);
            }
            // Even an all-zero sequential block needs a DC code and an AC end-of-block
            // code (at least two bits). Reject impossible dimensions before allocation.
            if (minimumBlocks > (long)(_data.Length - _pos) * 4)
                throw new InvalidDataException("JPEG dimensions require more scan data than the file contains.");
            foreach (var (id, h, v, tq) in raw)
                _components.Add(new JpegComponent(id, h, v, tq, mcusPerLine * h, mcusPerColumn * v));
        }

        private void ReadHuffmanTables()
        {
            var body = ReadSegmentBody();
            if (body.IsEmpty)
                throw new InvalidDataException("Empty DHT segment.");
            while (!body.IsEmpty)
            {
                if (body.Length < 17)
                    throw new InvalidDataException("Truncated Huffman table header.");
                byte tcTh = body[0];
                int tableClass = tcTh >> 4, id = tcTh & 15;
                if (tableClass > 1 || id > 3)
                    throw new InvalidDataException("Corrupt Huffman table header.");

                var counts = new byte[16];
                int total = 0;
                for (int i = 0; i < 16; i++)
                {
                    counts[i] = body[1 + i];
                    total += counts[i];
                }
                if (total == 0 || total > 256 || total > body.Length - 17)
                    throw new InvalidDataException("Corrupt Huffman table.");

                var symbols = body.Slice(17, total).ToArray();
                body = body.Slice(17 + total);

                var table = new HuffmanTable(counts, symbols);
                if (tableClass == 0) _dcTables[id] = table; else _acTables[id] = table;
            }
        }

        private void ReadQuantizationTables()
        {
            var body = ReadSegmentBody();
            if (body.IsEmpty)
                throw new InvalidDataException("Empty DQT segment.");
            while (!body.IsEmpty)
            {
                byte pqTq = body[0];
                int precision = pqTq >> 4, id = pqTq & 15;
                if (precision > 1 || id > 3)
                    throw new InvalidDataException("Corrupt quantisation table header.");
                int tableBytes = 64 * (precision + 1);
                if (body.Length - 1 < tableBytes)
                    throw new InvalidDataException("Truncated quantisation table.");

                var table = new ushort[64];
                for (int i = 0; i < 64; i++)
                {
                    table[i] = precision == 0 ? body[1 + i] : BinaryPrimitives.ReadUInt16BigEndian(body.Slice(1 + 2 * i));
                    if (table[i] == 0)
                        throw new InvalidDataException("Quantisation values must be nonzero.");
                }
                if (_quantTables[id] != null && !table.AsSpan().SequenceEqual(_quantTables[id]))
                {
                    foreach (var component in _components)
                        if (component.QuantizationTableId == id && _scannedComponents.Contains(component.Id))
                            throw new NotSupportedException("Redefining a quantisation table after its component scan is not supported.");
                }
                _quantTables[id] = table;
                body = body.Slice(1 + tableBytes);
            }
        }

        private void ReadRestartInterval()
        {
            var body = ReadSegmentBody();
            if (body.Length != 2)
                throw new InvalidDataException("Corrupt DRI segment.");
            _restartInterval = BinaryPrimitives.ReadUInt16BigEndian(body);
        }

        private void ReadScan()
        {
            if (!_frameSeen)
                throw new InvalidDataException("Scan before frame header.");

            var body = ReadSegmentBody();
            if (body.IsEmpty)
                throw new InvalidDataException("Corrupt scan header.");
            int count = body[0];
            if (count < 1 || count > 4 || body.Length != 4 + 2 * count)
                throw new InvalidDataException("Corrupt scan header.");

            var scanComponents = new (JpegComponent Component, HuffmanTable Dc, HuffmanTable Ac)[count];
            int blocksPerMcu = 0;
            for (int i = 0; i < count; i++)
            {
                byte id = body[1 + 2 * i];
                byte tables = body[2 + 2 * i];
                if (!_scannedComponents.Add(id))
                    throw new InvalidDataException("Duplicate sequential scan component.");
                var component = _components.Find(c => c.Id == id)
                    ?? throw new InvalidDataException($"Scan references unknown component {id}.");
                int maxTableId = _frameMarker == JpegMarker.Sof0 ? 1 : 3;
                if ((tables >> 4) > maxTableId || (tables & 15) > maxTableId)
                    throw new InvalidDataException("Invalid Huffman table selector.");
                var dc = _dcTables[tables >> 4] ?? throw new InvalidDataException("Missing DC Huffman table.");
                var ac = _acTables[tables & 15] ?? throw new InvalidDataException("Missing AC Huffman table.");
                if (_quantTables[component.QuantizationTableId] == null)
                    throw new InvalidDataException("Missing quantisation table.");
                scanComponents[i] = (component, dc, ac);
                blocksPerMcu += component.HorizontalSampling * component.VerticalSampling;
            }

            if (count > 1 && blocksPerMcu > 10)
                throw new InvalidDataException("Interleaved scans may contain at most ten blocks per MCU.");

            if (body[^3] != 0 || body[^2] != 63 || body[^1] != 0)
                throw new InvalidDataException("Invalid sequential scan parameters.");

            var reader = new BitReader(_data, _pos);
            var predictors = new int[count];

            if (count == 1)
                DecodeNonInterleaved(reader, scanComponents[0], predictors);
            else
                DecodeInterleaved(reader, scanComponents, predictors);

            _pos = reader.FinishScan();
        }

        private void DecodeNonInterleaved(BitReader reader, (JpegComponent Component, HuffmanTable Dc, HuffmanTable Ac) sc, int[] predictors)
        {
            int hMax = MaxHorizontal(), vMax = MaxVertical();
            int blocksPerLine = CeilDiv(CeilDiv(_width * sc.Component.HorizontalSampling, hMax), 8);
            int blocksPerColumn = CeilDiv(CeilDiv(_height * sc.Component.VerticalSampling, vMax), 8);
            int total = blocksPerLine * blocksPerColumn;

            for (int i = 0; i < total; i++)
            {
                if (_restartInterval > 0 && i > 0 && i % _restartInterval == 0)
                {
                    reader.Restart();
                    predictors[0] = 0;
                }

                DecodeBlock(reader, sc.Dc, sc.Ac, ref predictors[0], sc.Component.Block(i / blocksPerLine, i % blocksPerLine));
            }
        }

        private void DecodeInterleaved(BitReader reader, (JpegComponent Component, HuffmanTable Dc, HuffmanTable Ac)[] scanComponents, int[] predictors)
        {
            int hMax = MaxHorizontal(), vMax = MaxVertical();
            int mcusPerLine = CeilDiv(_width, 8 * hMax);
            int mcusPerColumn = CeilDiv(_height, 8 * vMax);
            int total = mcusPerLine * mcusPerColumn;

            for (int mcu = 0; mcu < total; mcu++)
            {
                if (_restartInterval > 0 && mcu > 0 && mcu % _restartInterval == 0)
                {
                    reader.Restart();
                    Array.Clear(predictors);
                }

                int mcuRow = mcu / mcusPerLine, mcuCol = mcu % mcusPerLine;
                for (int c = 0; c < scanComponents.Length; c++)
                {
                    var (component, dc, ac) = scanComponents[c];
                    for (int v = 0; v < component.VerticalSampling; v++)
                    {
                        for (int h = 0; h < component.HorizontalSampling; h++)
                        {
                            var block = component.Block(mcuRow * component.VerticalSampling + v, mcuCol * component.HorizontalSampling + h);
                            DecodeBlock(reader, dc, ac, ref predictors[c], block);
                        }
                    }
                }
            }
        }

        private static void DecodeBlock(BitReader reader, HuffmanTable dc, HuffmanTable ac, ref int predictor, Span<short> block)
        {
            int t = dc.Decode(reader);
            if (t > 11)
                throw new InvalidDataException("Invalid DC category for 8-bit JPEG.");
            int diff = t == 0 ? 0 : Extend(reader.ReadBits(t), t);
            predictor += diff;
            if (predictor < -1024 || predictor > 1023)
                throw new InvalidDataException("DC coefficient exceeds the 8-bit JPEG range.");
            block[0] = (short)predictor;

            int k = 1;
            while (k < 64)
            {
                int rs = ac.Decode(reader);
                int run = rs >> 4, size = rs & 15;
                if (size > 10 || (size == 0 && run != 0 && run != 15))
                    throw new InvalidDataException("Invalid AC symbol for sequential 8-bit JPEG.");
                if (size == 0)
                {
                    if (run == 15)
                    {
                        k += 16;
                        if (k > 64)
                            throw new InvalidDataException("Zero run exceeds the coefficient block.");
                        continue;
                    }
                    break;
                }

                k += run;
                if (k > 63)
                    throw new InvalidDataException("Coefficient index out of range.");
                block[k] = (short)Extend(reader.ReadBits(size), size);
                k++;
            }
        }

        private static int Extend(int value, int bits) => value < (1 << (bits - 1)) ? value - (1 << bits) + 1 : value;

        private int MaxHorizontal()
        {
            int max = 1;
            foreach (var c in _components) max = Math.Max(max, c.HorizontalSampling);
            return max;
        }

        private int MaxVertical()
        {
            int max = 1;
            foreach (var c in _components) max = Math.Max(max, c.VerticalSampling);
            return max;
        }

        private static int CeilDiv(int a, int b) => (a + b - 1) / b;
    }
}
