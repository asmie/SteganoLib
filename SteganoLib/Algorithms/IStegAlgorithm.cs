namespace SteganoLib.Algorithms
{
    /// <summary>
    /// Embeds raw bytes into a carrier and reads them back.
    /// Implementations own the framing they need to recover the payload length.
    /// </summary>
    /// <typeparam name="TCarrier">Carrier type, e.g. an image or a PCM buffer.</typeparam>
    public interface IStegAlgorithm<in TCarrier>
    {
        /// <summary>Embed <paramref name="data"/> into <paramref name="carrier"/> in place.</summary>
        /// <exception cref="CapacityExceededException">The carrier cannot hold <paramref name="data"/>.</exception>
        void EmbedBytes(byte[] data, TCarrier carrier);

        /// <summary>Read back a previously embedded payload. Returns an empty array when none is found.</summary>
        byte[] ExtractBytes(TCarrier carrier);

        /// <summary>
        /// Payload length budget in bytes for the current settings. Zero may mean that
        /// required framing cannot fit. Algorithms with payload-dependent costs may
        /// reject messages within this budget; consult the implementation's contract.
        /// </summary>
        long Capacity(TCarrier carrier);

        /// <summary>
        /// Check length and framing constraints. Implementations that require framing
        /// even for empty payloads must override this check. Payload-dependent constraints
        /// such as forbidden trellis changes are evaluated during embedding.
        /// </summary>
        bool IsPossibleToEmbed(long dataLength, TCarrier carrier)
        {
            return dataLength >= 0 && dataLength <= Capacity(carrier);
        }
    }
}
