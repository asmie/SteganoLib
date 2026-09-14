using System;
using System.IO;

namespace SteganoLib.Jpeg
{
    /// <summary>Huffman table as stored in a DHT segment: code counts per length and the symbols in code order.</summary>
    internal sealed class HuffmanTable
    {
        private readonly int[] _minCode = new int[17];
        private readonly int[] _maxCode = new int[18];
        private readonly int[] _valPtr = new int[17];

        // Encoder side: code and length per symbol.
        private readonly ushort[] _code = new ushort[256];
        private readonly byte[] _size = new byte[256];

        public HuffmanTable(byte[] counts, byte[] symbols)
        {
            if (counts == null || counts.Length != 16)
                throw new ArgumentException("Need 16 code counts.", nameof(counts));
            if (symbols == null)
                throw new ArgumentNullException(nameof(symbols));

            Counts = counts;
            Symbols = symbols;
            Build();
        }

        public byte[] Counts { get; }

        public byte[] Symbols { get; }

        private void Build()
        {
            int total = 0;
            foreach (var c in Counts) total += c;
            if (total == 0 || total != Symbols.Length || total > 256)
                throw new InvalidDataException("Huffman table counts do not match symbol list.");

            int code = 0;
            int k = 0;
            var used = new bool[256];
            for (int length = 1; length <= 16; length++)
            {
                int n = Counts[length - 1];
                // T.81 Annex C reserves the all-ones code at every length.
                if (n > 0 && code + n >= (1 << length))
                    throw new InvalidDataException("Oversubscribed Huffman table or reserved all-ones code.");
                if (n == 0)
                {
                    _maxCode[length] = -1;
                }
                else
                {
                    _valPtr[length] = k;
                    _minCode[length] = code;
                    for (int i = 0; i < n; i++)
                    {
                        if (used[Symbols[k]])
                            throw new InvalidDataException("Duplicate Huffman symbol.");
                        used[Symbols[k]] = true;
                        _code[Symbols[k]] = (ushort)code;
                        _size[Symbols[k]] = (byte)length;
                        code++;
                        k++;
                    }
                    _maxCode[length] = code - 1;
                }
                code <<= 1;
            }
            _maxCode[17] = int.MaxValue;
        }

        public int Decode(BitReader reader)
        {
            int code = 0;
            for (int length = 1; length <= 16; length++)
            {
                code = (code << 1) | reader.ReadBit();
                if (_maxCode[length] >= 0 && code >= _minCode[length] && code <= _maxCode[length])
                    return Symbols[_valPtr[length] + code - _minCode[length]];
            }

            throw new InvalidDataException("Invalid Huffman code in scan data.");
        }

        public void Encode(BitWriter writer, int symbol)
        {
            int size = _size[symbol];
            if (size == 0)
                throw new InvalidOperationException($"Symbol 0x{symbol:X2} has no Huffman code.");
            writer.WriteBits(_code[symbol], size);
        }

        /// <summary>Build a length-limited optimal table from symbol frequencies (JPEG Annex K.2).</summary>
        public static HuffmanTable FromFrequencies(long[] frequencies)
        {
            if (frequencies == null || frequencies.Length != 256)
                throw new ArgumentException("Need 256 frequencies.", nameof(frequencies));

            var freq = new long[257];
            Array.Copy(frequencies, freq, 256);
            freq[256] = 1; // reserved so no real symbol gets the all-ones code

            var codeSize = new int[257];
            var others = new int[257];
            Array.Fill(others, -1);

            while (true)
            {
                int c1 = -1;
                long v = long.MaxValue;
                for (int i = 0; i <= 256; i++)
                {
                    if (freq[i] != 0 && freq[i] <= v)
                    {
                        v = freq[i];
                        c1 = i;
                    }
                }

                int c2 = -1;
                v = long.MaxValue;
                for (int i = 0; i <= 256; i++)
                {
                    if (freq[i] != 0 && freq[i] <= v && i != c1)
                    {
                        v = freq[i];
                        c2 = i;
                    }
                }

                if (c2 < 0)
                    break;

                freq[c1] += freq[c2];
                freq[c2] = 0;

                codeSize[c1]++;
                while (others[c1] >= 0)
                {
                    c1 = others[c1];
                    codeSize[c1]++;
                }

                others[c1] = c2;

                codeSize[c2]++;
                while (others[c2] >= 0)
                {
                    c2 = others[c2];
                    codeSize[c2]++;
                }
            }

            // With 257 symbols a code can in theory be 256 bits long before folding.
            var bits = new int[258];
            for (int i = 0; i <= 256; i++)
            {
                if (codeSize[i] > 0)
                    bits[codeSize[i]]++;
            }

            // Fold code lengths above 16 down (Annex K.3).
            for (int i = bits.Length - 1; i > 16; i--)
            {
                while (bits[i] > 0)
                {
                    int j = i - 2;
                    while (bits[j] == 0)
                        j--;

                    bits[i] -= 2;
                    bits[i - 1]++;
                    bits[j + 1] += 2;
                    bits[j]--;
                }
            }

            int longest = 16;
            while (bits[longest] == 0)
                longest--;
            bits[longest]--; // drop the reserved symbol

            var counts = new byte[16];
            for (int i = 1; i <= 16; i++)
                counts[i - 1] = (byte)bits[i];

            int total = 0;
            foreach (var c in counts) total += c;

            var symbols = new byte[total];
            int p = 0;
            for (int length = 1; length < bits.Length; length++)
            {
                for (int symbol = 0; symbol < 256; symbol++)
                {
                    if (codeSize[symbol] == length)
                        symbols[p++] = (byte)symbol;
                }
            }

            return new HuffmanTable(counts, symbols);
        }
    }
}
