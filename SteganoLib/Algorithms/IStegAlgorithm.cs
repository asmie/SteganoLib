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
        /// <returns><c>false</c> when the carrier is too small.</returns>
        bool EmbedBytes(byte[] data, TCarrier carrier);

        /// <summary>Read back a previously embedded payload. Returns an empty array when none is found.</summary>
        byte[] ExtractBytes(TCarrier carrier);

        /// <summary>Whether <paramref name="dataLength"/> bytes fit into <paramref name="carrier"/>.</summary>
        bool IsPossibleToEmbed(int dataLength, TCarrier carrier);
    }
}
