using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SteganoLib.Algorithms
{
    public interface IStegAlgorithm
    {
        public bool EmbedBytes(byte[] data, Image<Rgba32> image);

        public byte[] ExtractBytes(Image<Rgba32> image);

        public bool IsPossibleToEmbed(int dataLength, Image<Rgba32> image);
    }
}
