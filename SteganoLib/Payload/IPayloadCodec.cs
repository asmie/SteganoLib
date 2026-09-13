using System;

using SteganoLib.Crypto;

namespace SteganoLib.Payload
{
    /// <summary>
    /// Protects the payload body inside a <see cref="PayloadEnvelope"/>.
    /// A codec must detect a wrong key or a modified body in <see cref="TryOpen"/>.
    /// </summary>
    public interface IPayloadCodec
    {
        /// <summary>Stable identifier written into the envelope header.</summary>
        byte Id { get; }

        byte[] Seal(ReadOnlySpan<byte> plaintext, StegoKey key);

        bool TryOpen(ReadOnlySpan<byte> sealedBody, StegoKey key, out byte[] plaintext);
    }
}
