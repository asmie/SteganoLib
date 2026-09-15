#nullable enable

namespace SteganoLib.Metadata
{
    /// <summary>
    /// Where each container format keeps its entries. Sender and receiver must agree
    /// on these; the defaults are fine unless the cover already uses them.
    /// Load validates the settings for the detected format. Later option changes do not
    /// affect an already loaded store; do not change options concurrently with loading.
    /// </summary>
    public sealed class MetadataOptions
    {
        /// <summary>PNG chunk type: a private ancillary type such as <c>meTa</c>, or <c>tEXt</c>, <c>zTXt</c>, <c>iTXt</c> for text mode.</summary>
        public string PngChunkType { get; set; } = PngMetadataStore.DefaultChunkType;

        /// <summary>Keyword for PNG text mode.</summary>
        public string PngKeyword { get; set; } = PngMetadataStore.DefaultKeyword;

        /// <summary>JPEG APPn marker number, 0 to 15.</summary>
        public int JpegAppNumber { get; set; } = JpegMetadataStore.DefaultAppNumber;

        /// <summary>ASCII identifier at the start of each JPEG segment; empty for none.</summary>
        public string JpegIdentifier { get; set; } = JpegMetadataStore.DefaultIdentifier;

        /// <summary>Four-character RIFF chunk id for WAVE files.</summary>
        public string WavChunkId { get; set; } = WavMetadataStore.DefaultChunkId;
    }
}
