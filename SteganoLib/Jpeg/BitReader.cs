using System.IO;

namespace SteganoLib.Jpeg
{
    /// <summary>
    /// Reads entropy-coded data. Unstuffs 0xFF00, stops at any marker and feeds
    /// zero bits from then on, the way libjpeg tolerates a short scan.
    /// </summary>
    internal sealed class BitReader
    {
        private readonly byte[] _data;
        private int _pos;
        private uint _buffer;
        private int _bitCount;
        private int _markerPos = -1;

        public BitReader(byte[] data, int start)
        {
            _data = data;
            _pos = start;
        }

        /// <summary>Offset of the marker that ended the data, or the end of the buffer.</summary>
        public int EndPosition => _markerPos >= 0 ? _markerPos : _pos;

        public bool MarkerHit => _markerPos >= 0;

        public int ReadBit()
        {
            if (_bitCount == 0)
                Fill();

            _bitCount--;
            return (int)((_buffer >> _bitCount) & 1);
        }

        public int ReadBits(int count)
        {
            int value = 0;
            for (int i = 0; i < count; i++)
                value = (value << 1) | ReadBit();
            return value;
        }

        /// <summary>Consume the pending RSTn marker and restart bit alignment.</summary>
        public void Restart()
        {
            _buffer = 0;
            _bitCount = 0;

            if (_markerPos < 0)
            {
                // Data ran exactly to the marker; find it from the current position.
                SkipToMarker();
            }

            if (_markerPos < 0 || _markerPos + 1 >= _data.Length || !JpegMarker.IsRestart(_data[_markerPos + 1]))
                throw new InvalidDataException("Expected a restart marker.");

            _pos = _markerPos + 2;
            _markerPos = -1;
        }

        private void SkipToMarker()
        {
            while (_pos + 1 < _data.Length)
            {
                if (_data[_pos] == JpegMarker.Prefix && _data[_pos + 1] != JpegMarker.Stuffing && _data[_pos + 1] != JpegMarker.Prefix)
                {
                    _markerPos = _pos;
                    return;
                }
                _pos++;
            }
            _markerPos = _data.Length;
        }

        private void Fill()
        {
            if (_markerPos >= 0 || _pos >= _data.Length)
            {
                if (_markerPos < 0)
                    _markerPos = _data.Length;
                _buffer = 0;
                _bitCount = 8;
                return;
            }

            byte b = _data[_pos];
            if (b == JpegMarker.Prefix)
            {
                int next = _pos + 1 < _data.Length ? _data[_pos + 1] : JpegMarker.Eoi;
                if (next == JpegMarker.Stuffing)
                {
                    _pos += 2;
                }
                else if (next == JpegMarker.Prefix)
                {
                    // Fill byte; skip and retry.
                    _pos++;
                    Fill();
                    return;
                }
                else
                {
                    _markerPos = _pos;
                    _buffer = 0;
                    _bitCount = 8;
                    return;
                }
            }
            else
            {
                _pos++;
            }

            _buffer = b;
            _bitCount = 8;
        }
    }
}
