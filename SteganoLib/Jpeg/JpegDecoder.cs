using System;
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
        private bool _scanSeen;

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
                        if (!_scanSeen)
                            throw new InvalidDataException("JPEG has no scan data.");
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
                        _segments.Add(new JpegSegment(marker, ReadSegmentBody()));
                        break;

                    default:
                        if (JpegMarker.IsApp(marker))
                        {
                            _segments.Add(new JpegSegment(marker, ReadSegmentBody()));
                        }
                        else if (JpegMarker.IsSof(marker))
                        {
                            throw new NotSupportedException(marker == JpegMarker.Sof2
                                ? "Progressive JPEG is not supported."
                                : $"JPEG process 0x{marker:X2} is not supported; only baseline Huffman is.");
                        }
                        else if (JpegMarker.IsRestart(marker))
                        {
                            // Stray restart marker between segments; nothing to read.
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
            while (_pos < _data.Length && _data[_pos] != JpegMarker.Prefix)
                _pos++;
            while (_pos < _data.Length && _data[_pos] == JpegMarker.Prefix)
                _pos++;
            if (_pos >= _data.Length)
                throw new InvalidDataException("Unexpected end of JPEG data.");

            return _data[_pos++];
        }

        private int ReadUInt16()
        {
            if (_pos + 2 > _data.Length)
                throw new InvalidDataException("Unexpected end of JPEG data.");
            int value = (_data[_pos] << 8) | _data[_pos + 1];
            _pos += 2;
            return value;
        }

        private byte ReadByte()
        {
            if (_pos >= _data.Length)
                throw new InvalidDataException("Unexpected end of JPEG data.");
            return _data[_pos++];
        }

        private byte[] ReadSegmentBody()
        {
            int length = ReadUInt16();
            if (length < 2 || _pos + length - 2 > _data.Length)
                throw new InvalidDataException("Corrupt segment length.");

            var body = new byte[length - 2];
            Array.Copy(_data, _pos, body, 0, body.Length);
            _pos += body.Length;
            return body;
        }

        private void ReadFrame(byte marker)
        {
            if (_frameSeen)
                throw new InvalidDataException("Multiple frame headers.");
            _frameSeen = true;
            _frameMarker = marker;

            int length = ReadUInt16();
            int precision = ReadByte();
            if (precision != 8)
                throw new NotSupportedException($"{precision}-bit JPEG is not supported.");

            _height = ReadUInt16();
            _width = ReadUInt16();
            int count = ReadByte();
            if (_width == 0 || _height == 0)
                throw new NotSupportedException("JPEG with deferred height (DNL) is not supported.");
            if (count < 1 || count > 4 || length != 8 + 3 * count)
                throw new InvalidDataException("Corrupt frame header.");

            var raw = new (byte Id, int H, int V, int Tq)[count];
            int hMax = 1, vMax = 1;
            for (int i = 0; i < count; i++)
            {
                byte id = ReadByte();
                byte hv = ReadByte();
                byte tq = ReadByte();
                int h = hv >> 4, v = hv & 15;
                if (h < 1 || h > 4 || v < 1 || v > 4 || tq > 3)
                    throw new InvalidDataException("Corrupt component descriptor.");
                raw[i] = (id, h, v, tq);
                hMax = Math.Max(hMax, h);
                vMax = Math.Max(vMax, v);
            }

            int mcusPerLine = CeilDiv(_width, 8 * hMax);
            int mcusPerColumn = CeilDiv(_height, 8 * vMax);
            foreach (var (id, h, v, tq) in raw)
                _components.Add(new JpegComponent(id, h, v, tq, mcusPerLine * h, mcusPerColumn * v));
        }

        private void ReadHuffmanTables()
        {
            int length = ReadUInt16();
            int end = _pos + length - 2;
            while (_pos < end)
            {
                byte tcTh = ReadByte();
                int tableClass = tcTh >> 4, id = tcTh & 15;
                if (tableClass > 1 || id > 3)
                    throw new InvalidDataException("Corrupt Huffman table header.");

                var counts = new byte[16];
                int total = 0;
                for (int i = 0; i < 16; i++)
                {
                    counts[i] = ReadByte();
                    total += counts[i];
                }
                if (total > 256 || _pos + total > _data.Length)
                    throw new InvalidDataException("Corrupt Huffman table.");

                var symbols = new byte[total];
                Array.Copy(_data, _pos, symbols, 0, total);
                _pos += total;

                var table = new HuffmanTable(counts, symbols);
                if (tableClass == 0) _dcTables[id] = table; else _acTables[id] = table;
            }
            if (_pos != end)
                throw new InvalidDataException("Corrupt DHT segment.");
        }

        private void ReadQuantizationTables()
        {
            int length = ReadUInt16();
            int end = _pos + length - 2;
            while (_pos < end)
            {
                byte pqTq = ReadByte();
                int precision = pqTq >> 4, id = pqTq & 15;
                if (precision > 1 || id > 3)
                    throw new InvalidDataException("Corrupt quantisation table header.");

                var table = new ushort[64];
                for (int i = 0; i < 64; i++)
                    table[i] = precision == 0 ? ReadByte() : (ushort)ReadUInt16();
                _quantTables[id] = table;
            }
            if (_pos != end)
                throw new InvalidDataException("Corrupt DQT segment.");
        }

        private void ReadRestartInterval()
        {
            if (ReadUInt16() != 4)
                throw new InvalidDataException("Corrupt DRI segment.");
            _restartInterval = ReadUInt16();
        }

        private void ReadScan()
        {
            if (!_frameSeen)
                throw new InvalidDataException("Scan before frame header.");

            int length = ReadUInt16();
            int count = ReadByte();
            if (count < 1 || count > 4 || length != 6 + 2 * count)
                throw new InvalidDataException("Corrupt scan header.");

            var scanComponents = new (JpegComponent Component, HuffmanTable Dc, HuffmanTable Ac)[count];
            for (int i = 0; i < count; i++)
            {
                byte id = ReadByte();
                byte tables = ReadByte();
                var component = _components.Find(c => c.Id == id)
                    ?? throw new InvalidDataException($"Scan references unknown component {id}.");
                var dc = _dcTables[tables >> 4] ?? throw new InvalidDataException("Missing DC Huffman table.");
                var ac = _acTables[tables & 15] ?? throw new InvalidDataException("Missing AC Huffman table.");
                scanComponents[i] = (component, dc, ac);
            }

            int spectralStart = ReadByte();
            int spectralEnd = ReadByte();
            ReadByte(); // successive approximation, unused in sequential mode
            if (spectralStart != 0 || spectralEnd != 63)
                throw new NotSupportedException("Spectral selection is only valid in progressive JPEG.");

            var reader = new BitReader(_data, _pos);
            var predictors = new int[count];
            _scanSeen = true;

            if (count == 1)
                DecodeNonInterleaved(reader, scanComponents[0], predictors);
            else
                DecodeInterleaved(reader, scanComponents, predictors);

            _pos = reader.EndPosition;
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
            int diff = t == 0 ? 0 : Extend(reader.ReadBits(t), t);
            predictor += diff;
            block[0] = (short)predictor;

            int k = 1;
            while (k < 64)
            {
                int rs = ac.Decode(reader);
                int run = rs >> 4, size = rs & 15;
                if (size == 0)
                {
                    if (run == 15)
                    {
                        k += 16;
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
