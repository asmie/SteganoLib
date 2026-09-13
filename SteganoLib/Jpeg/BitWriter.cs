using System.IO;

namespace SteganoLib.Jpeg
{
    /// <summary>Writes entropy-coded data with 0xFF byte stuffing.</summary>
    internal sealed class BitWriter
    {
        private readonly Stream _output;
        private uint _buffer;
        private int _bitCount;

        public BitWriter(Stream output)
        {
            _output = output;
        }

        public void WriteBits(int value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                _buffer = (_buffer << 1) | (uint)((value >> i) & 1);
                _bitCount++;
                if (_bitCount == 8)
                    FlushByte();
            }
        }

        /// <summary>Pad the last byte with one bits, as the standard requires.</summary>
        public void Flush()
        {
            while (_bitCount != 0)
                WriteBits(1, 1);
        }

        public void WriteMarker(byte marker)
        {
            Flush();
            _output.WriteByte(JpegMarker.Prefix);
            _output.WriteByte(marker);
        }

        private void FlushByte()
        {
            byte b = (byte)_buffer;
            _output.WriteByte(b);
            if (b == JpegMarker.Prefix)
                _output.WriteByte(JpegMarker.Stuffing);
            _buffer = 0;
            _bitCount = 0;
        }
    }
}
