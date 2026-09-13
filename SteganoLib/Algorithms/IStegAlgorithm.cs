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

        /// <summary>Largest payload, in bytes, that <paramref name="carrier"/> can hold with the current settings.</summary>
        long Capacity(TCarrier carrier);

        /// <summary>Whether <paramref name="dataLength"/> bytes fit into <paramref name="carrier"/>.</summary>
        bool IsPossibleToEmbed(long dataLength, TCarrier carrier)
        {
            return dataLength >= 0 && dataLength <= Capacity(carrier);
        }
    }
}
