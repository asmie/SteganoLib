using System;
using System.Collections;
using System.Collections.Generic;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SteganoLib.Algorithms
{
    public class LSB : IStegAlgorithm
    {

        public bool EmbedBytes(byte[] data, Image<Rgba32> image)
        {
            if (ColumnSequenceGenerator == null)
                throw new InvalidOperationException("ColumnSequenceGenerator has not been set.");
            if (RowSequenceGenerator == null)
                throw new InvalidOperationException("RowSequenceGenerator has not been set.");

            if (!IsPossibleToEmbed(data.Length, image))
                return false;

            // Prepend 4-byte big-endian length header
            byte[] combined = new byte[4 + data.Length];
            combined[0] = (byte)(data.Length >> 24);
            combined[1] = (byte)(data.Length >> 16);
            combined[2] = (byte)(data.Length >> 8);
            combined[3] = (byte)(data.Length);
            Array.Copy(data, 0, combined, 4, data.Length);

            BitArray bits = new BitArray(combined);
            bool RUsed = true, GUsed = true, BUsed = true;
            int x = 0, y = 0;
            var usedBits = 0;
            var usedPixels = new HashSet<(int, int)>();

            for (var i = 0; i < bits.Length; i++)
			{
                if ((RUsed && GUsed && BUsed) || usedBits == ModifyMaxBitsInByte)
                {
                    bool found = false;
                    while (!found)
                    {
                        x = ColumnSequenceGenerator.Next(image.Width);
                        y = RowSequenceGenerator.Next(image.Height);

                        if (!usedPixels.Contains((x, y)))
						{
                            usedPixels.Add((x, y));
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
            if (ColumnSequenceGenerator == null)
                throw new InvalidOperationException("ColumnSequenceGenerator has not been set.");
            if (RowSequenceGenerator == null)
                throw new InvalidOperationException("RowSequenceGenerator has not been set.");

            bool RUsed = true, GUsed = true, BUsed = true;
            int x = 0, y = 0;
            var usedBits = 0;
            var usedPixels = new HashSet<(int, int)>();
            var extractedBits = new List<bool>();
            int bitsNeeded = 32; // Start with 4-byte length header

            int bitIndex = 0;
            while (bitIndex < bitsNeeded)
            {
                if ((RUsed && GUsed && BUsed) || usedBits == ModifyMaxBitsInByte)
                {
                    bool found = false;
                    while (!found)
                    {
                        x = ColumnSequenceGenerator.Next(image.Width);
                        y = RowSequenceGenerator.Next(image.Height);

                        if (!usedPixels.Contains((x, y)))
                        {
                            usedPixels.Add((x, y));
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
                    extractedBits.Add((pixel.R & 1) == 1);
                    RUsed = true;
                }
                else if (!GUsed)
                {
                    extractedBits.Add((pixel.G & 1) == 1);
                    GUsed = true;
                }
                else if (!BUsed)
                {
                    extractedBits.Add((pixel.B & 1) == 1);
                    BUsed = true;
                }

                usedBits++;
                bitIndex++;

                // After extracting header bits, decode the length
                if (bitIndex == 32)
                {
                    byte[] lengthBytes = new byte[4];
                    for (int byteIdx = 0; byteIdx < 4; byteIdx++)
                    {
                        for (int bi = 0; bi < 8; bi++)
                        {
                            if (extractedBits[byteIdx * 8 + bi])
                                lengthBytes[byteIdx] |= (byte)(1 << bi);
                        }
                    }

                    int dataLength = (lengthBytes[0] << 24) | (lengthBytes[1] << 16)
                                    | (lengthBytes[2] << 8) | lengthBytes[3];

                    if (dataLength < 0 || !IsPossibleToEmbed(dataLength, image))
                        return Array.Empty<byte>();

                    bitsNeeded = (dataLength + 4) * 8;
                }
            }

            // Reconstruct payload bytes from extracted bits (skip 32 header bits)
            int payloadLength = (bitsNeeded - 32) / 8;
            byte[] result = new byte[payloadLength];
            for (int byteIdx = 0; byteIdx < payloadLength; byteIdx++)
            {
                int baseBit = (byteIdx + 4) * 8;
                for (int bi = 0; bi < 8; bi++)
                {
                    if (extractedBits[baseBit + bi])
                        result[byteIdx] |= (byte)(1 << bi);
                }
            }

            return result;
        }

        public bool IsPossibleToEmbed(int dataLength, Image<Rgba32> image)
        {
            int enabledChannels = (ModifyR ? 1 : 0) + (ModifyG ? 1 : 0) + (ModifyB ? 1 : 0);
            if (enabledChannels == 0)
                return false;

            int totalBits = (dataLength + 4) * 8;
            int pixelsPerCycle = (enabledChannels + ModifyMaxBitsInByte - 1) / ModifyMaxBitsInByte;
            int fullCycles = totalBits / enabledChannels;
            int remainingBits = totalBits % enabledChannels;
            int pixelsForRemaining = remainingBits > 0
                ? (remainingBits + ModifyMaxBitsInByte - 1) / ModifyMaxBitsInByte
                : 0;
            int totalPixelsNeeded = fullCycles * pixelsPerCycle + pixelsForRemaining;

            return totalPixelsNeeded <= image.Width * image.Height;
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
