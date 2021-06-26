using System;
using System.Collections.Generic;
using System.Text;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace SteganoLib.Algorithms
{
    public class LSB : IStegAlgorithm
    {


        public Image<Rgba32> EmbedBytes(byte[] data, Image<Rgba32> image)
        {
            var dataLenBytes = data.Length;



            return image;
        }


        public byte[] ExtractBytes(Image<Rgba32> image)
        {
            var message = new byte[16];


            return message;
        }

        public bool IsPossibleToEmbed(byte[] data, Image<Rgba32> image)
        {

            return false;
        }
    }
}
