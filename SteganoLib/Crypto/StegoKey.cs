using System;
using System.Security.Cryptography;
using System.Text;

namespace SteganoLib.Crypto
{
    /// <summary>
    /// Master key shared by the sender and the receiver. Every component that needs
    /// key material asks for a purpose-bound subkey via <see cref="Derive"/>, so the
    /// pixel permutation and the payload codec never reuse the same bytes.
    /// </summary>
    public sealed class StegoKey
    {
        private const int MasterSize = 32;

        // Fixed salt: the receiver has no way to learn a random one before the
        // pixel permutation is known. Pass your own salt when you can share it.
        private static readonly byte[] DefaultSalt = Encoding.ASCII.GetBytes("SteganoLib.StegoKey.v1");

        private readonly byte[] _master;

        private StegoKey(byte[] master)
        {
            _master = master;
        }

        /// <summary>Wrap raw key bytes. Any length is accepted; 32 random bytes is the recommended input.</summary>
        public static StegoKey FromBytes(ReadOnlySpan<byte> key)
        {
            if (key.IsEmpty)
                throw new ArgumentException("Key must not be empty.", nameof(key));

            return new StegoKey(HKDF.Extract(HashAlgorithmName.SHA256, key.ToArray(), DefaultSalt));
        }

        /// <summary>Derive a key from a passphrase with PBKDF2-SHA256.</summary>
        public static StegoKey FromPassphrase(string passphrase, byte[] salt = null, int iterations = 600_000)
        {
            if (string.IsNullOrEmpty(passphrase))
                throw new ArgumentException("Passphrase must not be empty.", nameof(passphrase));
            if (iterations < 1)
                throw new ArgumentOutOfRangeException(nameof(iterations));

            var master = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(passphrase),
                salt ?? DefaultSalt,
                iterations,
                HashAlgorithmName.SHA256,
                MasterSize);

            return new StegoKey(master);
        }

        /// <summary>Generate a fresh random key.</summary>
        public static StegoKey CreateRandom()
        {
            return FromBytes(RandomNumberGenerator.GetBytes(MasterSize));
        }

        /// <summary>Derive <paramref name="length"/> bytes bound to <paramref name="purpose"/> (HKDF-SHA256 expand).</summary>
        public byte[] Derive(string purpose, int length)
        {
            if (string.IsNullOrEmpty(purpose))
                throw new ArgumentException("Purpose must not be empty.", nameof(purpose));
            if (length < 1)
                throw new ArgumentOutOfRangeException(nameof(length));

            return HKDF.Expand(HashAlgorithmName.SHA256, _master, length, Encoding.UTF8.GetBytes(purpose));
        }
    }
}
