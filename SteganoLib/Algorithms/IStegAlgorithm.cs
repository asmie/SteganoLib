using System;
using System.Collections.Generic;
using System.Text;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace libSteganography.Algorithms
{
    public interface IStegAlgorithm
    {
        public Image<Rgba32> EmbedBytes(byte[] data, Image<Rgba32> image);

        public byte[] ExtractBytes(Image<Rgba32> image);

        public bool IsPossibleToEmbed(byte[] data, Image<Rgba32> image);
    }
}
