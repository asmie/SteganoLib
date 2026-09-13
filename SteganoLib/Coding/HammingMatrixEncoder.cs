using System;

namespace SteganoLib.Coding
{
    /// <summary>
    /// (1, 2^k - 1, k) matrix encoding as used by F5: k message bits are carried by
    /// n = 2^k - 1 cover bits and at most one of them has to change. The syndrome of
    /// a word is the XOR of the 1-based positions of its set bits.
    /// </summary>
    public sealed class HammingMatrixEncoder
    {
        public HammingMatrixEncoder(int k)
        {
            if (k < 1 || k > 15)
                throw new ArgumentOutOfRangeException(nameof(k), "k must be between 1 and 15.");

            K = k;
            N = (1 << k) - 1;
        }

        /// <summary>Message bits per code word.</summary>
        public int K { get; }

        /// <summary>Cover bits per code word.</summary>
        public int N { get; }

        /// <summary>Message currently carried by <paramref name="word"/>.</summary>
        public int Syndrome(ReadOnlySpan<bool> word)
        {
            if (word.Length != N)
                throw new ArgumentException($"Word must have {N} bits.", nameof(word));

            int syndrome = 0;
            for (int i = 0; i < word.Length; i++)
            {
                if (word[i])
                    syndrome ^= i + 1;
            }
            return syndrome;
        }

        /// <summary>
        /// 1-based position of the single bit to flip so the word carries
        /// <paramref name="message"/>, or 0 when it already does.
        /// </summary>
        public int PositionToFlip(ReadOnlySpan<bool> word, int message)
        {
            if (message < 0 || message > N)
                throw new ArgumentOutOfRangeException(nameof(message), $"Message must fit in {K} bits.");

            return Syndrome(word) ^ message;
        }

        /// <summary>Change density: expected changes per embedded bit, ignoring shrinkage.</summary>
        public double ChangesPerBit => (double)N / (N + 1) / K;

        /// <summary>Embedding rate in message bits per cover bit.</summary>
        public double Rate => (double)K / N;
    }
}
