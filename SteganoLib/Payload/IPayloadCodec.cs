#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;

using SteganoLib.Crypto;

namespace SteganoLib.Payload
{
    /// <summary>
    /// Protects the payload body inside a <see cref="PayloadEnvelope"/>.
    /// A codec must detect a wrong key, a modified body or modified associated data in
    /// <see cref="TryOpen"/>. The envelope passes its own header as the associated data,
    /// so flags and version travel in the clear but cannot be changed unnoticed.
    /// Codec identifiers and overhead must remain unchanged after registration. Codecs
    /// must not mutate or retain input spans and must return independently owned output arrays.
    /// </summary>
    public interface IPayloadCodec
    {
        /// <summary>Stable identifier written into the envelope header.</summary>
        byte Id { get; }

        /// <summary>Fixed, nonnegative number of bytes added to the plaintext by <see cref="Seal"/>.</summary>
        int Overhead { get; }

        byte[] Seal(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData, StegoKey key);

        /// <summary>Returns true only with authenticated, non-null plaintext; failure output is ignored.</summary>
        bool TryOpen(ReadOnlySpan<byte> sealedBody, ReadOnlySpan<byte> associatedData, StegoKey key, [NotNullWhen(true)] out byte[]? plaintext);
    }
}
