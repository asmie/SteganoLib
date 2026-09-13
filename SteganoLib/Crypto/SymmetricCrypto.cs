using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SteganoLib.Crypto
{
    /// <summary>
    /// Authenticated symmetric encryption on top of any registered
    /// <see cref="SymmetricAlgorithm"/> (AES by default). Encrypt-then-MAC with
    /// HMAC-SHA256, a fresh IV per message and, for passphrase keys, a fresh salt.
    /// <para>
    /// Output layout: version (1), flags (1), [salt (16) when the key is derived],
    /// IV (block size), ciphertext, HMAC-SHA256 tag (32).
    /// </para>
    /// </summary>
    public sealed class SymmetricCrypto
    {
        private const byte FormatVersion = 1;
        private const byte FlagDerivedKey = 0x01;
        private const int SaltSize = 16;
        private const int TagSize = 32;
        private const int HeaderSize = 2;
        private static readonly byte[] MacInfo = Encoding.ASCII.GetBytes("SteganoLib/SymmetricCrypto/mac/v1");

        /// <summary>How <see cref="Key"/> is interpreted.</summary>
        public enum KeyTypes
        {
            /// <summary>Raw cipher key of a size the algorithm accepts.</summary>
            Plain,

            /// <summary>Passphrase; the cipher and MAC keys are derived with PBKDF2 and a per-message salt.</summary>
            RFC2898Derived
        }

        public byte[] EncryptText(string plain)
        {
            if (plain == null)
                throw new ArgumentNullException(nameof(plain));

            return EncryptMemory(Encoding.UTF8.GetBytes(plain));
        }

        public byte[] EncryptMemory(byte[] plain)
        {
            if (plain == null)
                throw new ArgumentNullException(nameof(plain));
            if (plain.Length == 0)
                throw new ArgumentException("Input is empty.", nameof(plain));
            EnsureKey();
            EnsureMode();

            using var algorithm = CreateInstance(Algorithm);

            byte flags = 0;
            byte[] salt = null;
            if (KeyType == KeyTypes.RFC2898Derived)
            {
                flags |= FlagDerivedKey;
                salt = RandomNumberGenerator.GetBytes(SaltSize);
            }

            var (cipherKey, macKey) = DeriveKeys(algorithm, salt);
            algorithm.Key = cipherKey;
            algorithm.Mode = Mode;
            algorithm.Padding = Padding;
            algorithm.GenerateIV();

            byte[] ciphertext;
            using (var encryptor = algorithm.CreateEncryptor())
                ciphertext = encryptor.TransformFinalBlock(plain, 0, plain.Length);

            int ivSize = algorithm.BlockSize / 8;
            int saltSize = salt?.Length ?? 0;
            var output = new byte[HeaderSize + saltSize + ivSize + ciphertext.Length + TagSize];

            output[0] = FormatVersion;
            output[1] = flags;
            int offset = HeaderSize;
            if (salt != null)
            {
                salt.CopyTo(output, offset);
                offset += saltSize;
            }
            algorithm.IV.CopyTo(output, offset);
            offset += ivSize;
            ciphertext.CopyTo(output, offset);
            offset += ciphertext.Length;

            HMACSHA256.HashData(macKey, output.AsSpan(0, offset), output.AsSpan(offset, TagSize));
            return output;
        }

        public string DecryptText(byte[] encrypted)
        {
            return Encoding.UTF8.GetString(DecryptMemory(encrypted));
        }

        /// <exception cref="CryptographicException">Wrong key, modified data, or unknown format.</exception>
        public byte[] DecryptMemory(byte[] encrypted)
        {
            if (encrypted == null)
                throw new ArgumentNullException(nameof(encrypted));
            if (encrypted.Length == 0)
                throw new ArgumentException("Input is empty.", nameof(encrypted));
            EnsureKey();
            EnsureMode();

            using var algorithm = CreateInstance(Algorithm);
            int ivSize = algorithm.BlockSize / 8;

            if (encrypted.Length < HeaderSize + ivSize + TagSize)
                throw new CryptographicException("Input is too short.");
            if (encrypted[0] != FormatVersion)
                throw new CryptographicException("Unsupported format version.");

            bool derived = (encrypted[1] & FlagDerivedKey) != 0;
            if (derived != (KeyType == KeyTypes.RFC2898Derived))
                throw new CryptographicException("Key type does not match the message.");

            int offset = HeaderSize;
            byte[] salt = null;
            if (derived)
            {
                if (encrypted.Length < HeaderSize + SaltSize + ivSize + TagSize)
                    throw new CryptographicException("Input is too short.");
                salt = encrypted.AsSpan(offset, SaltSize).ToArray();
                offset += SaltSize;
            }

            var (cipherKey, macKey) = DeriveKeys(algorithm, salt);

            int tagOffset = encrypted.Length - TagSize;
            Span<byte> expectedTag = stackalloc byte[TagSize];
            HMACSHA256.HashData(macKey, encrypted.AsSpan(0, tagOffset), expectedTag);
            if (!CryptographicOperations.FixedTimeEquals(expectedTag, encrypted.AsSpan(tagOffset, TagSize)))
                throw new CryptographicException("Authentication failed.");

            algorithm.Key = cipherKey;
            algorithm.Mode = Mode;
            algorithm.Padding = Padding;
            algorithm.IV = encrypted.AsSpan(offset, ivSize).ToArray();
            offset += ivSize;

            using var decryptor = algorithm.CreateDecryptor();
            return decryptor.TransformFinalBlock(encrypted, offset, tagOffset - offset);
        }

        /// <summary>PBKDF2 iteration count for <see cref="KeyTypes.RFC2898Derived"/>. Default 600,000 (OWASP 2023).</summary>
        public int Iterations
        {
            get => _iterations;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _iterations = value;
            }
        }

        /// <summary>Hash used inside PBKDF2. Default SHA-256.</summary>
        public HashAlgorithmName HashAlgorithm { get; set; } = HashAlgorithmName.SHA256;

        public KeyTypes KeyType { get; set; } = KeyTypes.RFC2898Derived;

        /// <summary>Raw key or passphrase bytes. Write-only so a holder of this object cannot read it back.</summary>
        public byte[] Key
        {
            private get;
            set;
        }

        /// <summary>Registered algorithm name. Default "AES".</summary>
        public string Algorithm { get; set; } = "AES";

        /// <summary>Cipher mode. ECB is rejected. Default CBC.</summary>
        public CipherMode Mode { get; set; } = CipherMode.CBC;

        public PaddingMode Padding { get; set; } = PaddingMode.PKCS7;

        private int _iterations = 600_000;

        private void EnsureKey()
        {
            if (Key == null || Key.Length == 0)
                throw new InvalidOperationException("Key has not been set.");
        }

        private void EnsureMode()
        {
            if (Mode == CipherMode.ECB)
                throw new InvalidOperationException("ECB mode is not supported.");
        }

        private (byte[] CipherKey, byte[] MacKey) DeriveKeys(SymmetricAlgorithm algorithm, byte[] salt)
        {
            int cipherKeySize = algorithm.KeySize / 8;

            if (KeyType == KeyTypes.RFC2898Derived)
            {
                var material = Rfc2898DeriveBytes.Pbkdf2(Key, salt, Iterations, HashAlgorithm, cipherKeySize + TagSize);
                return (material.AsSpan(0, cipherKeySize).ToArray(), material.AsSpan(cipherKeySize).ToArray());
            }

            return (Key, HKDF.DeriveKey(HashAlgorithmName.SHA256, Key, TagSize, salt: null, info: MacInfo));
        }

        /// <exception cref="InvalidOperationException">The name is not registered.</exception>
        private static SymmetricAlgorithm CreateInstance(string name)
        {
            if (name == null || !_registeredAlgorithms.TryGetValue(name, out var type))
                throw new InvalidOperationException($"Symmetric algorithm '{name}' is not registered.");

            if (type == typeof(Aes))
                return Aes.Create();
            return (SymmetricAlgorithm)Activator.CreateInstance(type);
        }

        /// <summary>
        /// Register a <see cref="SymmetricAlgorithm"/> under a name. Names are unique;
        /// a second registration with the same name returns <c>false</c> and changes nothing.
        /// </summary>
        public static bool RegisterAlgorithm(string name, Type creator)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            if (creator == null)
                throw new ArgumentNullException(nameof(creator));
            if (!creator.IsSubclassOf(typeof(SymmetricAlgorithm)))
                return false;

            return _registeredAlgorithms.TryAdd(name, creator);
        }

        private static readonly ConcurrentDictionary<string, Type> _registeredAlgorithms = new(
            new[]
            {
                new KeyValuePair<string, Type>("AES", typeof(Aes)),
            });
    }
}
