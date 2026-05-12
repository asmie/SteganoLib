using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace SteganoLib.Crypto
{
    /// <summary>
    /// Main class for symmetric cryptography. Class is responsible for handling static information about available algorithms as well as
    /// encrypting and decrypting real data.
    /// Keys are stored in the way that user give them.
    /// </summary>
    public sealed class SymmetricCrypto
    {
        /// <summary>
        /// Enumerates possible key types.
        /// </summary>
        public enum KeyTypes
        {
            Plain,
            RFC2898Derived
        }

        /// <summary>
        /// Default constructor.
        /// </summary>
        public SymmetricCrypto()
        {

        }

        /// <summary>
        /// Encrypt text.
        /// </summary>
        /// <param name="plain">Text to be encrypted.</param>
        /// <returns>Byte array with ciphertext.</returns>
        public byte[] EncryptText(string plain)
        {
            return EncryptMemory(Encoding.UTF8.GetBytes(plain));
        }

        /// <summary>
        /// Encrypt given memory.
        /// </summary>
        /// <param name="plain">Memory to be encrypted.</param>
        /// <returns>Byte array with encrypted memory.</returns>
        public byte[] EncryptMemory(byte[] plain)
        {
            byte[] encrypted;

            if (plain == null || plain.Length <= 0)
                throw new ArgumentNullException("plain empty");
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException("key empty or invalid size");

            using (var myAlgo = CreateInstance(Algorithm))
            {
                if (KeyType == KeyTypes.RFC2898Derived)
                    myAlgo.Key = Rfc2898DeriveBytes.Pbkdf2(Key, Salt, Iterations, HashAlgorithm, myAlgo.KeySize / 8);
                else
                    myAlgo.Key = Key;

                myAlgo.Mode = Mode;
                myAlgo.IV = IV;
                myAlgo.Padding = Padding;

                ICryptoTransform encryptor = myAlgo.CreateEncryptor(myAlgo.Key, myAlgo.IV);

                using var msEncrypt = new MemoryStream();
                using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                {
                    csEncrypt.Write(plain, 0, plain.Length);
                    csEncrypt.Close();
                }

                encrypted = msEncrypt.ToArray();
            }

            return encrypted;
        }

        /// <summary>
        /// Decrypt ciphertext into plain text.
        /// </summary>
        /// <param name="encrypted">Memory with ciphertext.</param>
        /// <returns>String with decrypted text.</returns>
        public string DecryptText(byte[] encrypted)
        {
            return Encoding.UTF8.GetString(DecryptMemory(encrypted));
        }

        /// <summary>
        /// Decrypt encrypted memory.
        /// </summary>
        /// <param name="encrypted">Encrypted memory.</param>
        /// <returns>Decrypted memory.</returns>
        public byte[] DecryptMemory(byte[] encrypted)
        {
            byte[] plain;

            if (encrypted == null || encrypted.Length <= 0)
                throw new ArgumentNullException("encrypted argument empty");
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException("key empty or invalid");

            using (var myAlgo = CreateInstance(Algorithm))
            {
                if (KeyType == KeyTypes.RFC2898Derived)
                    myAlgo.Key = Rfc2898DeriveBytes.Pbkdf2(Key, Salt, Iterations, HashAlgorithm, myAlgo.KeySize / 8);
                else
                    myAlgo.Key = Key;

                myAlgo.Mode = Mode;
                myAlgo.IV = IV;
                myAlgo.Padding = Padding;


                ICryptoTransform decryptor = myAlgo.CreateDecryptor(myAlgo.Key, myAlgo.IV);

                using var msDecrypt = new MemoryStream();
                using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Write))
                {
                    csDecrypt.Write(encrypted, 0, encrypted.Length);
                    csDecrypt.Close();
                }

                plain = msDecrypt.ToArray();
            }

            return plain;
        }

        /// <summary>
        /// Salt used in RFC 2898 key derivation.
        /// </summary>
        public byte[] Salt
        {
            get; set;
        } = { 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0 };

        /// <summary>
        /// Iteration count for PBKDF2 key derivation (used when <see cref="KeyType"/> is
        /// <see cref="KeyTypes.RFC2898Derived"/>). Defaults to 600,000 per current OWASP guidance.
        /// Both encryptor and decryptor must use the same value.
        /// </summary>
        public int Iterations
        {
            get; set;
        } = 600_000;

        /// <summary>
        /// Hash algorithm used inside PBKDF2 key derivation. Defaults to SHA-256.
        /// Both encryptor and decryptor must use the same value.
        /// </summary>
        public HashAlgorithmName HashAlgorithm
        {
            get; set;
        } = HashAlgorithmName.SHA256;

        /// <summary>
        /// Key type.
        /// </summary>
        public KeyTypes KeyType
        {
            get; set;
        } = KeyTypes.RFC2898Derived;

        /// <summary>
        /// Byte array with key. Write-only by design — the getter is private so that
        /// callers holding a <see cref="SymmetricCrypto"/> reference cannot read back
        /// key material that was set on it.
        /// </summary>
        public byte[] Key
        {
            private get;
            set;
        } = { 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0 };

        /// <summary>
        /// Byte array with IV used in chain modes. Write-only by design — see <see cref="Key"/>.
        /// </summary>
        public byte[] IV
        {
            private get;
            set;
        } = { 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0 };

        /// <summary>
        /// Name of the algorithm to use during operations.
        /// </summary>
        public string Algorithm
        {
            get; set;
        } = "AES";

        /// <summary>
        /// Cipher mode.
        /// </summary>
        public CipherMode Mode
        {
            get; set;
        } = CipherMode.CBC;

        /// <summary>
        /// Determines if padding should be added to the data and if so, chooses type of padding.
        /// </summary>
        public PaddingMode Padding
        {
            get; set;
        } = PaddingMode.PKCS7;

        /// <summary>
        /// Static method that creates instance of algorithm that is chosen by name. Used internally by encrypt/decrypt methods.
        /// </summary>
        /// <param name="name">Algorithm name.</param>
        /// <returns>Created instance of algorithm or null if name was not found in the registered algorithms.</returns>
        private static SymmetricAlgorithm CreateInstance(string name)
        {
            if (_registeredAlgorithms.TryGetValue(name, out var type))
            {
                if (type == typeof(Aes))
                    return Aes.Create();
                return (SymmetricAlgorithm)Activator.CreateInstance(type);
            }

            return null;
        }

        /// <summary>
        /// Register a custom symmetric-algorithm implementation. The type must derive
        /// from <see cref="SymmetricAlgorithm"/>. Names are unique: re-registering an
        /// existing name returns <c>false</c> and leaves the existing registration
        /// untouched (no silent shadowing).
        /// </summary>
        /// <param name="name">Name of the algorithm.</param>
        /// <param name="creator">Type to be used for algorithm creation.</param>
        /// <returns><c>true</c> if registered; <c>false</c> if the type is not a <see cref="SymmetricAlgorithm"/> subclass, or if a registration with the same name already exists.</returns>
        public static bool RegisterAlgorithm(string name, Type creator)
        {
            if (!creator.IsSubclassOf(typeof(SymmetricAlgorithm)) && creator != typeof(SymmetricAlgorithm))
                return false;

            return _registeredAlgorithms.TryAdd(name, creator);
        }

        /// <summary>
        /// Currently registered symmetric algorithms, keyed by name.
        /// </summary>
        private static readonly ConcurrentDictionary<string, Type> _registeredAlgorithms = new(
            new[]
            {
                new KeyValuePair<string, Type>("AES", typeof(Aes)),
            });

    }
}
