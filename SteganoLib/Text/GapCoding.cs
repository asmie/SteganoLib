using System;
using System.Collections;

using SteganoLib.Algorithms;
using SteganoLib.Crypto;

namespace SteganoLib.Text
{
    /// <summary>
    /// Shared logic for methods that append a group of bits to each of several gaps
    /// (word boundaries, line ends). Gaps are filled in keyed order; a 4-byte
    /// big-endian length header precedes the payload. A gap holding fewer bits than
    /// the group size marks the end of the data.
    /// </summary>
    internal static class GapCoding
    {
        public const int HeaderSize = 4;

        public static long Capacity(long gapCount, int bitsPerGap) => Math.Max(0, gapCount * bitsPerGap / 8 - HeaderSize);

        /// <returns>Bits for each gap in text order; <c>null</c> for gaps that stay empty.</returns>
        public static bool[][] Distribute(byte[] data, int gapCount, int bitsPerGap, byte[] permutationKey)
        {
            long capacity = Capacity(gapCount, bitsPerGap);
            if (data.Length > capacity)
                throw new CapacityExceededException(data.Length, capacity);

            var framed = new byte[HeaderSize + data.Length];
            framed[0] = (byte)(data.Length >> 24);
            framed[1] = (byte)(data.Length >> 16);
            framed[2] = (byte)(data.Length >> 8);
            framed[3] = (byte)data.Length;
            Array.Copy(data, 0, framed, HeaderSize, data.Length);
            var bits = new BitArray(framed);

            var result = new bool[gapCount][];
            if (gapCount == 0)
                return result;

            var permutation = new FeistelPermutation(permutationKey, gapCount);
            int bit = 0;
            for (long i = 0; i < gapCount && bit < bits.Length; i++)
            {
                int take = Math.Min(bitsPerGap, bits.Length - bit);
                var group = new bool[take];
                for (int t = 0; t < take; t++)
                    group[t] = bits[bit++];
                result[permutation.Permute(i)] = group;
            }

            return result;
        }

        /// <summary>Reassemble the payload from the bits found at each gap in text order (empty array for none).</summary>
        public static byte[] Collect(bool[][] gapBits, int bitsPerGap, byte[] permutationKey)
        {
            int gapCount = gapBits.Length;
            if (gapCount == 0)
                return Array.Empty<byte>();

            var permutation = new FeistelPermutation(permutationKey, gapCount);
            var bits = new System.Collections.Generic.List<bool>();
            for (long i = 0; i < gapCount; i++)
            {
                var group = gapBits[permutation.Permute(i)];
                bits.AddRange(group);
                if (group.Length < bitsPerGap)
                    break;
            }

            if (bits.Count < HeaderSize * 8)
                return Array.Empty<byte>();

            var header = ToBytes(bits, 0, HeaderSize);
            int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (length < 0 || length > Capacity(gapCount, bitsPerGap) || (long)(HeaderSize + length) * 8 > bits.Count)
                return Array.Empty<byte>();

            return ToBytes(bits, HeaderSize * 8, length);
        }

        private static byte[] ToBytes(System.Collections.Generic.List<bool> bits, int startBit, int byteCount)
        {
            var bytes = new byte[byteCount];
            for (int i = 0; i < byteCount * 8; i++)
            {
                if (bits[startBit + i])
                    bytes[i / 8] |= (byte)(1 << (i % 8));
            }
            return bytes;
        }
    }
}
