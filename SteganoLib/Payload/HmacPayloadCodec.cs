#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

using SteganoLib.Crypto;

namespace SteganoLib.Payload
{
    /// <summary>
    /// Integrity only: plaintext followed by an HMAC-SHA256 tag over the associated data
    /// and the plaintext. Use when the data is already encrypted and you only need to
    /// detect a wrong key or tampering.
    /// </summary>
    public sealed class HmacPayloadCodec : IPayloadCodec
    {
        private const string Purpose = "SteganoLib/payload/hmac-sha256/v1";
        private const int KeySize = 32;
        private const int TagSize = 32;

        public byte Id => 2;

        public int Overhead => TagSize;

        public byte[] Seal(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData, StegoKey key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (plaintext.Length > Array.MaxLength - Overhead)
                throw new ArgumentOutOfRangeException(nameof(plaintext), "Sealed data exceeds the maximum byte array length.");
            var output = new byte[plaintext.Length + TagSize];
            plaintext.CopyTo(output);
            Tag(key, associatedData, plaintext, output.AsSpan(plaintext.Length));
            return output;
        }

        public bool TryOpen(ReadOnlySpan<byte> sealedBody, ReadOnlySpan<byte> associatedData, StegoKey key, [NotNullWhen(true)] out byte[]? plaintext)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            plaintext = null;
            if (sealedBody.Length < TagSize)
                return false;

            var body = sealedBody.Slice(0, sealedBody.Length - TagSize);
            var tag = sealedBody.Slice(body.Length);

            Span<byte> expected = stackalloc byte[TagSize];
            Tag(key, associatedData, body, expected);

            if (!CryptographicOperations.FixedTimeEquals(expected, tag))
                return false;

            plaintext = body.ToArray();
            return true;
        }

        /// <summary>HMAC over the associated data's length, the associated data, then the body, so the two cannot trade bytes.</summary>
        private static void Tag(StegoKey key, ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> body, Span<byte> destination)
        {
            using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key.Derive(Purpose, KeySize));
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, associatedData.Length);
            hmac.AppendData(length);
            hmac.AppendData(associatedData);
            hmac.AppendData(body);
            hmac.GetHashAndReset(destination);
        }
    }
}
