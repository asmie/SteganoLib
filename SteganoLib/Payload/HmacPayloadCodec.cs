using System;
using System.Security.Cryptography;

using SteganoLib.Crypto;

namespace SteganoLib.Payload
{
    /// <summary>
    /// Integrity only: plaintext followed by an HMAC-SHA256 tag. Use when the data
    /// is already encrypted and you only need to detect a wrong key or tampering.
    /// </summary>
    public sealed class HmacPayloadCodec : IPayloadCodec
    {
        private const string Purpose = "SteganoLib/payload/hmac-sha256/v1";
        private const int KeySize = 32;
        private const int TagSize = 32;

        public byte Id => 2;

        public byte[] Seal(ReadOnlySpan<byte> plaintext, StegoKey key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            var output = new byte[plaintext.Length + TagSize];
            plaintext.CopyTo(output);
            HMACSHA256.HashData(key.Derive(Purpose, KeySize), plaintext, output.AsSpan(plaintext.Length));
            return output;
        }

        public bool TryOpen(ReadOnlySpan<byte> sealedBody, StegoKey key, out byte[] plaintext)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            plaintext = null;
            if (sealedBody.Length < TagSize)
                return false;

            var body = sealedBody.Slice(0, sealedBody.Length - TagSize);
            var tag = sealedBody.Slice(body.Length);

            Span<byte> expected = stackalloc byte[TagSize];
            HMACSHA256.HashData(key.Derive(Purpose, KeySize), body, expected);

            if (!CryptographicOperations.FixedTimeEquals(expected, tag))
                return false;

            plaintext = body.ToArray();
            return true;
        }
    }
}
