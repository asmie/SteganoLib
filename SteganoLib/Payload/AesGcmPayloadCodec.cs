#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

using SteganoLib.Crypto;

namespace SteganoLib.Payload
{
    /// <summary>AES-256-GCM with the associated data bound into the tag. Body layout: 12-byte nonce, ciphertext, 16-byte tag.</summary>
    public sealed class AesGcmPayloadCodec : IPayloadCodec
    {
        private const string Purpose = "SteganoLib/payload/aes-gcm/v1";
        private const int KeySize = 32;
        private const int NonceSize = 12;
        private const int TagSize = 16;

        public byte Id => 1;

        public int Overhead => NonceSize + TagSize;

        public byte[] Seal(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData, StegoKey key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (plaintext.Length > Array.MaxLength - Overhead)
                throw new ArgumentOutOfRangeException(nameof(plaintext), "Sealed data exceeds the maximum byte array length.");
            var output = new byte[NonceSize + plaintext.Length + TagSize];
            var nonce = output.AsSpan(0, NonceSize);
            var ciphertext = output.AsSpan(NonceSize, plaintext.Length);
            var tag = output.AsSpan(NonceSize + plaintext.Length, TagSize);

            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(key.Derive(Purpose, KeySize), TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

            return output;
        }

        public bool TryOpen(ReadOnlySpan<byte> sealedBody, ReadOnlySpan<byte> associatedData, StegoKey key, [NotNullWhen(true)] out byte[]? plaintext)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            plaintext = null;
            if (sealedBody.Length < NonceSize + TagSize)
                return false;

            var nonce = sealedBody.Slice(0, NonceSize);
            var ciphertext = sealedBody.Slice(NonceSize, sealedBody.Length - NonceSize - TagSize);
            var tag = sealedBody.Slice(sealedBody.Length - TagSize, TagSize);

            var result = new byte[ciphertext.Length];
            try
            {
                using var aes = new AesGcm(key.Derive(Purpose, KeySize), TagSize);
                aes.Decrypt(nonce, ciphertext, tag, result, associatedData);
            }
            catch (AuthenticationTagMismatchException)
            {
                return false;
            }

            plaintext = result;
            return true;
        }
    }
}
