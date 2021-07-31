using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace SteganoLib.Algorithms
{
    public class LSB : IStegAlgorithm
    {

        public bool EmbedBytes(byte[] data, ref Image<Rgba32> image)
        {
            BitArray bits = new BitArray(data);
            if (!IsPossibleToEmbed(data.Length, image))
                return false;

            bool RUsed = true, GUsed = true, BUsed = true;
            int x = 0, y = 0;
            var usedBits = 0;
            List<Tuple<int, int>> map = new List<Tuple<int, int>>();

            for (var i = 0; i < bits.Length; i++)
			{
                if ((RUsed && GUsed && BUsed) || usedBits == ModifyMaxBitsInByte)
                {
                    bool found = false;
                    while (found)
                    {
                        x = RowSequenceGenerator.Next();
                        y = ColumnSequenceGenerator.Next();

                        var pixelCoords = new Tuple<int, int>(x, y);

                        if (! map.Contains(pixelCoords))
						{
                            map.Add(pixelCoords);
                            found = true;
						}
                    }

                    if (RUsed && GUsed && BUsed)
                    {
                        if (ModifyR) RUsed = false;
                        if (ModifyG) GUsed = false;
                        if (ModifyB) BUsed = false;
                    }

                    usedBits = 0;
                }

                var pixel = image[x, y];

                if (!RUsed)
                {
                    pixel.R = (byte)(bits[i] ? (pixel.R | 1) : (pixel.R & 254));
                    RUsed = true;
                }
                else if (!GUsed)
				{
                    pixel.G = (byte)(bits[i] ? (pixel.G | 1) : (pixel.G & 254));
                    GUsed = true;
                }
                else if (!BUsed)
				{
                    pixel.B = (byte)(bits[i] ? (pixel.B | 1) : (pixel.B & 254));
                    BUsed = true;
                }

                usedBits++;

                image[x, y] = pixel;
            }

            return true;
        }


        public byte[] ExtractBytes(Image<Rgba32> image)
        {
            var message = new byte[16];


            return message;
        }

        public bool IsPossibleToEmbed(int dataLength, Image<Rgba32> image)
        {

            return false;
        }

        public bool ModifyR { get; set; } = true;
        public bool ModifyG { get; set; } = true;
        public bool ModifyB { get; set; } = true;

        public int ModifyMaxBitsInByte { get; set; } = 1;


        public Crypto.PRNG RowSequenceGenerator
        {
            get; set;
        }

        public Crypto.PRNG ColumnSequenceGenerator
        {
            get; set;
        }

    }
}
