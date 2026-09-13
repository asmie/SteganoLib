using System;
using System.Collections;
using System.Collections.Generic;

using SteganoLib.Coding;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;

namespace SteganoLib.Algorithms
{
    /// <summary>
    /// F5 steganography for JPEG (Westfeld, 2001). Message bits ride on the parity of
    /// non-zero AC coefficients, visited in a keyed order. A coefficient that has to
    /// change is moved one step toward zero; if it becomes zero it no longer counts
    /// and the bit is re-embedded on the next one (shrinkage). Payload bits use
    /// (1, 2^k - 1, k) matrix encoding so few coefficients change; a small header
    /// carrying k and the length is embedded first with k = 1.
    /// </summary>
    public sealed class F5 : IStegAlgorithm<JpegImage>
    {
        private const string Purpose = "SteganoLib/f5-permutation/v1";
        private const int HeaderSize = 5;
        private const int HeaderBits = HeaderSize * 8;
        private const int AcPerBlock = 63;

        private readonly byte[] _keyMaterial;
        private int _maxK = 7;

        public F5(StegoKey key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            _keyMaterial = key.Derive(Purpose, FeistelPermutation.KeySize);
        }

        /// <summary>Largest matrix-encoding parameter to consider. 1 disables matrix encoding. Default 7.</summary>
        public int MaxK
        {
            get => _maxK;
            set
            {
                if (value < 1 || value > 15)
                    throw new ArgumentOutOfRangeException(nameof(value), "MaxK must be between 1 and 15.");
                _maxK = value;
            }
        }

        /// <inheritdoc />
        public void EmbedBytes(byte[] data, JpegImage image)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            long capacity = Capacity(image);
            if (data.Length > capacity)
                throw new CapacityExceededException(data.Length, capacity);

            var arrays = Arrays(image);
            for (int k = ChooseK(image, data.Length); k >= 1; k--)
            {
                var work = new short[arrays.Length][];
                for (int i = 0; i < arrays.Length; i++)
                    work[i] = (short[])arrays[i].Clone();

                if (TryEmbed(work, image, data, k))
                {
                    for (int i = 0; i < arrays.Length; i++)
                        Array.Copy(work[i], arrays[i], arrays[i].Length);
                    return;
                }
            }

            throw new CapacityExceededException(data.Length, capacity);
        }

        /// <inheritdoc />
        public byte[] ExtractBytes(JpegImage image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            var arrays = Arrays(image);
            using var refs = NonZero(arrays, image).GetEnumerator();

            var header = new byte[HeaderSize];
            if (!ReadBits(refs, 1, header, HeaderBits))
                return Array.Empty<byte>();

            int k = header[0];
            int length = (header[1] << 24) | (header[2] << 16) | (header[3] << 8) | header[4];
            if (k < 1 || k > 15 || length < 0 || (long)length * 8 > CountNonZero(arrays).NonZero)
                return Array.Empty<byte>();

            var payload = new byte[length];
            if (!ReadBits(refs, k, payload, (long)length * 8))
                return Array.Empty<byte>();

            return payload;
        }

        /// <summary>
        /// Guaranteed capacity: every non-zero AC coefficient carries a bit, and only
        /// coefficients of magnitude one can be lost to shrinkage. Matrix encoding
        /// does not raise this bound, it only reduces the number of changes.
        /// </summary>
        public long Capacity(JpegImage image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            var (nonZero, ones) = CountNonZero(Arrays(image));
            long bits = nonZero - ones - HeaderBits;
            return Math.Max(0, bits / 8);
        }

        /// <summary>Largest k (up to <see cref="MaxK"/>) whose estimated capacity covers <paramref name="payloadLength"/> bytes.</summary>
        public int ChooseK(JpegImage image, int payloadLength)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));
            if (payloadLength < 0)
                throw new ArgumentOutOfRangeException(nameof(payloadLength));

            var (nonZero, ones) = CountNonZero(Arrays(image));
            if (nonZero == 0)
                return 1;

            double pOne = (double)ones / nonZero;
            double available = nonZero - HeaderBits - HeaderBits * pOne;
            long needed = (long)payloadLength * 8;

            for (int k = _maxK; k > 1; k--)
            {
                int n = (1 << k) - 1;
                double shrink = (double)n / (n + 1) * pOne;
                double perWord = n + shrink / (1 - shrink);
                if (available / perWord * k >= needed)
                    return k;
            }
            return 1;
        }

        private bool TryEmbed(short[][] work, JpegImage image, byte[] data, int k)
        {
            var header = new byte[HeaderSize];
            header[0] = (byte)k;
            header[1] = (byte)(data.Length >> 24);
            header[2] = (byte)(data.Length >> 16);
            header[3] = (byte)(data.Length >> 8);
            header[4] = (byte)data.Length;

            using var refs = NonZero(work, image).GetEnumerator();
            return WriteBits(refs, 1, new BitArray(header))
                && WriteBits(refs, k, new BitArray(data));
        }

        private static bool WriteBits(IEnumerator<(short[] Array, int Index)> refs, int k, BitArray bits)
        {
            var code = new HammingMatrixEncoder(k);
            var word = new List<(short[] Array, int Index)>(code.N);
            var wordBits = new bool[code.N];

            for (int offset = 0; offset < bits.Length; offset += k)
            {
                int message = 0;
                for (int t = 0; t < k && offset + t < bits.Length; t++)
                    if (bits[offset + t]) message |= 1 << t;

                while (true)
                {
                    while (word.Count < code.N)
                    {
                        if (!refs.MoveNext())
                            return false;
                        word.Add(refs.Current);
                    }

                    for (int i = 0; i < code.N; i++)
                        wordBits[i] = Bit(word[i].Array[word[i].Index]);

                    int position = code.PositionToFlip(wordBits, message);
                    if (position == 0)
                        break;

                    var (array, index) = word[position - 1];
                    array[index] = StepTowardZero(array[index]);
                    if (array[index] != 0)
                        break;

                    word.RemoveAt(position - 1); // shrinkage: refill and try the same message again
                }

                word.Clear();
            }

            return true;
        }

        private static bool ReadBits(IEnumerator<(short[] Array, int Index)> refs, int k, byte[] target, long bitCount)
        {
            int n = (1 << k) - 1;
            long bit = 0;

            while (bit < bitCount)
            {
                int syndrome = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!refs.MoveNext())
                        return false;
                    var (array, index) = refs.Current;
                    if (Bit(array[index]))
                        syndrome ^= i + 1;
                }

                for (int t = 0; t < k && bit < bitCount; t++, bit++)
                {
                    if (((syndrome >> t) & 1) == 1)
                        target[bit / 8] |= (byte)(1 << (int)(bit % 8));
                }
            }

            return true;
        }

        // F5 convention: positive coefficients carry their LSB, negative ones the inverted LSB.
        // Moving one step toward zero flips the bit either way.
        private static bool Bit(short value) => value > 0 ? (value & 1) == 1 : (value & 1) == 0;

        private static short StepTowardZero(short value) => (short)(value > 0 ? value - 1 : value + 1);

        private static short[][] Arrays(JpegImage image)
        {
            var arrays = new short[image.Components.Count][];
            for (int i = 0; i < arrays.Length; i++)
                arrays[i] = image.Components[i].Coefficients;
            return arrays;
        }

        private static (long NonZero, long Ones) CountNonZero(short[][] arrays)
        {
            long nonZero = 0, ones = 0;
            foreach (var array in arrays)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    if ((i & 63) == 0)
                        continue;
                    int v = array[i];
                    if (v != 0)
                    {
                        nonZero++;
                        if (v == 1 || v == -1)
                            ones++;
                    }
                }
            }
            return (nonZero, ones);
        }

        // Non-zero AC coefficients in keyed order. DC terms are never used.
        private IEnumerable<(short[] Array, int Index)> NonZero(short[][] arrays, JpegImage image)
        {
            var offsets = new long[arrays.Length + 1];
            for (int c = 0; c < arrays.Length; c++)
                offsets[c + 1] = offsets[c] + (long)image.Components[c].BlockCount * AcPerBlock;

            long total = offsets[arrays.Length];
            if (total == 0)
                yield break;

            var permutation = new FeistelPermutation(_keyMaterial, total);
            for (long i = 0; i < total; i++)
            {
                long p = permutation.Permute(i);
                int c = 0;
                while (p >= offsets[c + 1])
                    c++;

                long local = p - offsets[c];
                int index = (int)(local / AcPerBlock * 64 + local % AcPerBlock + 1);
                if (arrays[c][index] != 0)
                    yield return (arrays[c], index);
            }
        }
    }
}
