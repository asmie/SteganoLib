using System;

namespace SteganoLib.Crypto
{
    /// <summary>
    /// Keyed bijection on <c>[0, Domain)</c>. A balanced Feistel network over the
    /// smallest even bit width covering the domain, with SipHash as round function
    /// and cycle walking to stay inside the domain. Stateless, so any index can be
    /// mapped in O(1) without tracking which values were already produced.
    /// </summary>
    public sealed class FeistelPermutation
    {
        private const int Rounds = 8;

        private readonly SipHash[] _roundFunctions;
        private readonly int _halfBits;
        private readonly ulong _halfMask;
        private readonly long _domain;

        /// <param name="key">At least 16 bytes. Round keys are sliced from it, so pass HKDF output rather than a raw passphrase.</param>
        /// <param name="domain">Number of elements to permute. Must be positive.</param>
        public FeistelPermutation(ReadOnlySpan<byte> key, long domain)
        {
            if (domain < 1)
                throw new ArgumentOutOfRangeException(nameof(domain));
            if (key.Length < KeySize)
                throw new ArgumentException($"Key must be at least {KeySize} bytes.", nameof(key));

            _domain = domain;

            int bits = 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)(domain - 1));
            if (bits < 2) bits = 2;
            if ((bits & 1) == 1) bits++;

            _halfBits = bits / 2;
            _halfMask = (1UL << _halfBits) - 1;

            _roundFunctions = new SipHash[Rounds];
            for (int i = 0; i < Rounds; i++)
                _roundFunctions[i] = new SipHash(key.Slice(i * 16, 16));
        }

        /// <summary>Bytes of key material consumed by the constructor.</summary>
        public static int KeySize => Rounds * 16;

        /// <summary>Size of the permuted domain.</summary>
        public long Domain => _domain;

        /// <summary>Map <paramref name="index"/> to its position in the permutation.</summary>
        public long Permute(long index)
        {
            if (index < 0 || index >= _domain)
                throw new ArgumentOutOfRangeException(nameof(index));

            ulong x = (ulong)index;
            do
            {
                x = Encrypt(x);
            } while ((long)x >= _domain);

            return (long)x;
        }

        private ulong Encrypt(ulong x)
        {
            ulong left = x >> _halfBits;
            ulong right = x & _halfMask;

            for (int r = 0; r < Rounds; r++)
            {
                ulong f = _roundFunctions[r].Hash(right) & _halfMask;
                ulong next = left ^ f;
                left = right;
                right = next;
            }

            return (left << _halfBits) | right;
        }
    }
}
