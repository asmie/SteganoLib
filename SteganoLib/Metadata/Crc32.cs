using System;

namespace SteganoLib.Metadata
{
    /// <summary>CRC-32 (IEEE 802.3, reflected, polynomial 0xEDB88320) as used by PNG and zlib.</summary>
    internal static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second = default)
        {
            uint crc = 0xFFFFFFFF;
            crc = Update(crc, first);
            crc = Update(crc, second);
            return crc ^ 0xFFFFFFFF;
        }

        private static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (byte b in data)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }
}
