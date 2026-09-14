using System;
using System.Collections.Generic;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;

namespace SteganoLib.Steganalysis
{
    /// <summary>One colour channel of an image as a row-major byte plane.</summary>
    internal static class ChannelPlane
    {
        public static byte[] Extract(Image<Rgba32> image, ColorChannels channel)
        {
            int index = channel switch
            {
                ColorChannels.Red => 0,
                ColorChannels.Green => 1,
                ColorChannels.Blue => 2,
                _ => throw new ArgumentException("Exactly one of Red, Green or Blue is required.", nameof(channel)),
            };

            var plane = new byte[image.Width * image.Height];
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    int offset = y * accessor.Width;
                    for (int x = 0; x < row.Length; x++)
                    {
                        var p = row[x];
                        plane[offset + x] = index == 0 ? p.R : index == 1 ? p.G : p.B;
                    }
                }
            });
            return plane;
        }

        /// <summary>The single channels present in <paramref name="channels"/>, in R, G, B order.</summary>
        public static IReadOnlyList<ColorChannels> Split(ColorChannels channels)
        {
            if ((channels & ColorChannels.All) == ColorChannels.None)
                throw new ArgumentException("At least one channel is required.", nameof(channels));

            var list = new List<ColorChannels>(3);
            if (channels.HasFlag(ColorChannels.Red)) list.Add(ColorChannels.Red);
            if (channels.HasFlag(ColorChannels.Green)) list.Add(ColorChannels.Green);
            if (channels.HasFlag(ColorChannels.Blue)) list.Add(ColorChannels.Blue);
            return list;
        }
    }
}
