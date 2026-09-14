using System;
using System.Buffers.Binary;
using System.Linq;

using SixLabors.ImageSharp.Formats.Jpeg;
using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;
using Xunit;

namespace SteganoLib.Test
{
    public class F5FramingTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 23, 41 });

        [Fact]
        public void ImpossibleTrellisWidth_IsRejectedBeforeAllocatingExpandedPayload()
        {
            var image = Framed(1, 8, 255, 64);
            var algorithm = new F5(Key);
            algorithm.ExtractBytes(image); // warm the extraction path before measuring allocations
            long before = GC.GetAllocatedBytesForCurrentThread();
            var result = algorithm.ExtractBytes(image);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Empty(result);
            Assert.True(allocated < 50_000, $"Impossible header allocated {allocated} bytes.");
        }

        [Theory]
        [InlineData(1, 0, 0, 8, 64)]
        [InlineData(3, 0, 0, 1, 21)] // eight bits need three complete seven-coefficient groups
        [InlineData(1, 3, 4, 2, 64)]
        public void Framing_RequiresHeaderAndEveryCompletePayloadGroup(byte k, byte height, byte width, int length, int coefficients)
        {
            var algorithm = new F5(Key);
            Assert.Equal(new byte[length], algorithm.ExtractBytes(Framed(k, height, width, length, 56 + coefficients)));
            Assert.Empty(algorithm.ExtractBytes(Framed(k, height, width, length, 55 + coefficients)));
        }

        [Theory]
        [InlineData(0, 0, 0, 1)]
        [InlineData(16, 0, 0, 1)]
        [InlineData(1, 0, 1, 1)]
        [InlineData(2, 3, 1, 1)]
        [InlineData(1, 1, 1, 1)]
        [InlineData(1, 13, 1, 1)]
        [InlineData(1, 3, 0, 1)]
        [InlineData(1, 3, 255, int.MaxValue)]
        [InlineData(15, 0, 0, int.MaxValue)]
        [InlineData(1, 0, 0, -1)]
        public void InvalidHeaderParameters_ReturnNoPayload(byte k, byte height, byte width, int length)
        {
            var image = Framed(k, height, width, length);
            var original = (short[])image.Components.Single().Coefficients.Clone();
            Assert.Empty(new F5(Key).ExtractBytes(image));
            Assert.Equal(original, image.Components.Single().Coefficients);
        }

        private static JpegImage Framed(byte k, byte height, byte width, int length, int nonZeroCount = 4032)
        {
            var image = JpegImage.Load(JpegImageTests.SampleJpeg(64, 64, JpegEncodingColor.Luminance));
            var component = image.Components.Single();
            Array.Clear(component.Coefficients);
            var order = new FeistelPermutation(Key.Derive("SteganoLib/f5-permutation/v1", FeistelPermutation.KeySize), component.BlockCount * 63);
            var header = new byte[] { k, height, width, 0, 0, 0, 0 };
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(3), length);
            for (int i = 0; i < nonZeroCount; i++)
            {
                long position = order.Permute(i);
                int index = (int)(position / 63 * 64 + position % 63 + 1);
                bool bit = i < 56 && (header[i / 8] & (1 << (i % 8))) != 0;
                component.Coefficients[index] = bit ? (short)3 : (short)2;
            }
            return image;
        }
    }
}
