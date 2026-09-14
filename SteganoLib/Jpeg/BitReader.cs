using System.IO;

namespace SteganoLib.Jpeg
{
    /// <summary>Reads entropy-coded data, unstuffs 0xFF00 and rejects truncated scans.</summary>
    internal sealed class BitReader
    {
        private readonly byte[] _data;
        private int _pos;
        private uint _buffer;
        private int _bitCount;
        private int _nextRestart;

        public BitReader(byte[] data, int start)
        {
            _data = data;
            _pos = start;
        }

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

        /// <summary>Validate padding and consume the next restart marker in the RST0–RST7 cycle.</summary>
        public void Restart()
        {
            Align();
            int marker = Marker();
            if (marker != JpegMarker.Rst0 + _nextRestart)
                throw new InvalidDataException("Missing or out-of-order restart marker.");
            _pos++;
            _nextRestart = (_nextRestart + 1) & 7;
        }

        /// <summary>Validate the scan's padding and return the following marker's offset.</summary>
        public int FinishScan()
        {
            Align();
            int start = _pos;
            Marker();
            return start;
        }

        private void Align()
        {
            uint mask = (1u << _bitCount) - 1;
            if ((_buffer & mask) != mask)
                throw new InvalidDataException("Invalid JPEG scan padding.");
            _buffer = 0;
            _bitCount = 0;
        }

        // Fill bytes are legal before a marker. Iterate so long runs cannot exhaust the stack.
        private byte Marker()
        {
            if (_pos >= _data.Length || _data[_pos] != JpegMarker.Prefix)
                throw new InvalidDataException("Expected a marker after JPEG scan data.");
            while (_pos < _data.Length && _data[_pos] == JpegMarker.Prefix)
                _pos++;
            if (_pos >= _data.Length || _data[_pos] == JpegMarker.Stuffing)
                throw new InvalidDataException("Missing JPEG scan marker.");
            return _data[_pos];
        }

        private void Fill()
        {
            if (_pos >= _data.Length)
                throw new InvalidDataException("Truncated JPEG scan data.");

            byte b = _data[_pos++];
            if (b == JpegMarker.Prefix)
            {
                if (_pos >= _data.Length || _data[_pos] != JpegMarker.Stuffing)
                    throw new InvalidDataException("Unexpected marker in JPEG scan data.");
                _pos++;
            }
            _buffer = b;
            _bitCount = 8;
        }
    }
}
