using System;
using System.Buffers.Binary;

namespace SteganoLib.Crypto
{
    /// <summary>SipHash-2-4 over a single 64-bit word. Used as the Feistel round function.</summary>
    internal readonly struct SipHash
    {
        private readonly ulong _k0;
        private readonly ulong _k1;

        public SipHash(ReadOnlySpan<byte> key128)
        {
            if (key128.Length != 16)
                throw new ArgumentException("SipHash key must be 16 bytes.", nameof(key128));

            _k0 = BinaryPrimitives.ReadUInt64LittleEndian(key128);
            _k1 = BinaryPrimitives.ReadUInt64LittleEndian(key128.Slice(8));
        }

        public ulong Hash(ulong m)
        {
            ulong v0 = _k0 ^ 0x736f6d6570736575UL;
            ulong v1 = _k1 ^ 0x646f72616e646f6dUL;
            ulong v2 = _k0 ^ 0x6c7967656e657261UL;
            ulong v3 = _k1 ^ 0x7465646279746573UL;

            v3 ^= m;
            Round(ref v0, ref v1, ref v2, ref v3);
            Round(ref v0, ref v1, ref v2, ref v3);
            v0 ^= m;

            // Length block: 8 bytes of message.
            ulong b = 8UL << 56;
            v3 ^= b;
            Round(ref v0, ref v1, ref v2, ref v3);
            Round(ref v0, ref v1, ref v2, ref v3);
            v0 ^= b;

            v2 ^= 0xff;
            Round(ref v0, ref v1, ref v2, ref v3);
            Round(ref v0, ref v1, ref v2, ref v3);
            Round(ref v0, ref v1, ref v2, ref v3);
            Round(ref v0, ref v1, ref v2, ref v3);

            return v0 ^ v1 ^ v2 ^ v3;
        }

        private static void Round(ref ulong v0, ref ulong v1, ref ulong v2, ref ulong v3)
        {
            v0 += v1; v1 = Rotl(v1, 13); v1 ^= v0; v0 = Rotl(v0, 32);
            v2 += v3; v3 = Rotl(v3, 16); v3 ^= v2;
            v0 += v3; v3 = Rotl(v3, 21); v3 ^= v0;
            v2 += v1; v1 = Rotl(v1, 17); v1 ^= v2; v2 = Rotl(v2, 32);
        }

        private static ulong Rotl(ulong x, int b) => (x << b) | (x >> (64 - b));
    }
}
