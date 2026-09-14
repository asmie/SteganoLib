using System;

using SteganoLib.Crypto;

namespace SteganoLib.Payload
{
    /// <summary>
    /// Protects the payload body inside a <see cref="PayloadEnvelope"/>.
    /// A codec must detect a wrong key, a modified body or modified associated data in
    /// <see cref="TryOpen"/>. The envelope passes its own header as the associated data,
    /// so flags and version travel in the clear but cannot be changed unnoticed.
    /// </summary>
    public interface IPayloadCodec
    {
        /// <summary>Stable identifier written into the envelope header.</summary>
        byte Id { get; }

        /// <summary>Bytes added to the plaintext by <see cref="Seal"/>.</summary>
        int Overhead { get; }

        byte[] Seal(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData, StegoKey key);

        bool TryOpen(ReadOnlySpan<byte> sealedBody, ReadOnlySpan<byte> associatedData, StegoKey key, out byte[] plaintext);
    }
}
