using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SteganoLib.Test
{
    public class LSBTest
    {
        private static Algorithms.LSB CreateLSB(int seed, bool modifyR = true, bool modifyG = true, bool modifyB = true, int maxBits = 1)
        {
            var rowPrng = new Crypto.PRNG();
            rowPrng.Name = "Random";
            rowPrng.Initialize(seed);

            var colPrng = new Crypto.PRNG();
            colPrng.Name = "Random";
            colPrng.Initialize(seed + 1);

            return new Algorithms.LSB
            {
                RowSequenceGenerator = rowPrng,
                ColumnSequenceGenerator = colPrng,
                ModifyR = modifyR,
                ModifyG = modifyG,
                ModifyB = modifyB,
                ModifyMaxBitsInByte = maxBits
            };
        }

        [Fact]
        public void IsPossibleToEmbed_SufficientCapacity_ReturnsTrue()
        {
            var lsb = CreateLSB(42);
            using var image = new Image<Rgba32>(100, 100);
            Assert.True(lsb.IsPossibleToEmbed(10, image));
        }

        [Fact]
        public void IsPossibleToEmbed_InsufficientCapacity_ReturnsFalse()
        {
            var lsb = CreateLSB(42);
            // 4 pixels total, need (1+4)*8 = 40 pixels with M=1
            using var image = new Image<Rgba32>(2, 2);
            Assert.False(lsb.IsPossibleToEmbed(1, image));
        }

        [Fact]
        public void IsPossibleToEmbed_ExactBoundary_ReturnsTrue()
        {
            var lsb = CreateLSB(42);
            // 1 byte data: need (1+4)*8 = 40 pixels with M=1, C=3
            using var image = new Image<Rgba32>(8, 5); // exactly 40 pixels
            Assert.True(lsb.IsPossibleToEmbed(1, image));
        }

        [Fact]
        public void IsPossibleToEmbed_SingleChannel_ReducedCapacity()
        {
            var lsb = CreateLSB(42, modifyG: false, modifyB: false);
            // C=1, M=1: each pixel contributes 1 bit, capacity = W*H bits
            using var image = new Image<Rgba32>(10, 10); // 100 pixels
            Assert.True(lsb.IsPossibleToEmbed(8, image));  // (8+4)*8 = 96 <= 100
            Assert.False(lsb.IsPossibleToEmbed(9, image)); // (9+4)*8 = 104 > 100
        }

        [Fact]
        public void IsPossibleToEmbed_NoChannelsEnabled_ReturnsFalse()
        {
            var lsb = CreateLSB(42, modifyR: false, modifyG: false, modifyB: false);
            using var image = new Image<Rgba32>(100, 100);
            Assert.False(lsb.IsPossibleToEmbed(1, image));
        }

        [Fact]
        public void EmbedBytes_ImageTooSmall_ReturnsFalse()
        {
            var lsb = CreateLSB(42);
            using var image = new Image<Rgba32>(2, 2);
            byte[] data = new byte[] { 0x41, 0x42, 0x43 };
            Assert.False(lsb.EmbedBytes(data, image));
        }

        public static IEnumerable<object[]> RoundTripData =>
        new List<object[]>
        {
            new object[] { new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }, 42 },      // "Hello"
            new object[] { new byte[] { 0xFF }, 123 },                               // single byte 0xFF
            new object[] { new byte[] { 0x00 }, 456 },                               // single byte 0x00
            new object[] { new byte[] { 0x00, 0x00, 0x00, 0x00 }, 789 },             // all zeros
            new object[] { new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, 1000 },            // all ones
        };

        [Theory]
        [MemberData(nameof(RoundTripData))]
        public void EmbedAndExtract_RoundTrip(byte[] data, int seed)
        {
            var embedLsb = CreateLSB(seed);
            using var image = new Image<Rgba32>(100, 100);

            Assert.True(embedLsb.EmbedBytes(data, image));

            var extractLsb = CreateLSB(seed);
            byte[] extracted = extractLsb.ExtractBytes(image);

            Assert.Equal(data, extracted);
        }

        [Fact]
        public void EmbedAndExtract_LargerData_RoundTrip()
        {
            var data = new byte[256];
            for (int i = 0; i < 256; i++)
                data[i] = (byte)i;

            var embedLsb = CreateLSB(42);
            using var image = new Image<Rgba32>(100, 100);

            Assert.True(embedLsb.EmbedBytes(data, image));

            var extractLsb = CreateLSB(42);
            byte[] extracted = extractLsb.ExtractBytes(image);

            Assert.Equal(data, extracted);
        }

        [Fact]
        public void EmbedAndExtract_SingleChannel_RoundTrip()
        {
            byte[] data = new byte[] { 0x41, 0x42 };

            var embedLsb = CreateLSB(42, modifyG: false, modifyB: false);
            using var image = new Image<Rgba32>(100, 100);

            Assert.True(embedLsb.EmbedBytes(data, image));

            var extractLsb = CreateLSB(42, modifyG: false, modifyB: false);
            byte[] extracted = extractLsb.ExtractBytes(image);

            Assert.Equal(data, extracted);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void EmbedAndExtract_MaxBitsGreaterThanOne_RoundTrip(int maxBits)
        {
            byte[] data = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };

            var embedLsb = CreateLSB(42, maxBits: maxBits);
            using var image = new Image<Rgba32>(100, 100);

            Assert.True(embedLsb.EmbedBytes(data, image));

            var extractLsb = CreateLSB(42, maxBits: maxBits);
            byte[] extracted = extractLsb.ExtractBytes(image);

            Assert.Equal(data, extracted);
        }

        [Fact]
        public void EmbedAndExtract_MaxBitsGreaterThanChannels_RoundTrip()
        {
            byte[] data = new byte[] { 0xAA, 0x55 };

            // M=4 > C=3: channels exhaust before bit limit, so behaves like M=C
            var embedLsb = CreateLSB(42, maxBits: 4);
            using var image = new Image<Rgba32>(100, 100);

            Assert.True(embedLsb.EmbedBytes(data, image));

            var extractLsb = CreateLSB(42, maxBits: 4);
            byte[] extracted = extractLsb.ExtractBytes(image);

            Assert.Equal(data, extracted);
        }

        [Fact]
        public void EmbedBytes_NullColumnGenerator_ThrowsInvalidOperation()
        {
            var lsb = new Algorithms.LSB
            {
                RowSequenceGenerator = new Crypto.PRNG()
            };
            using var image = new Image<Rgba32>(100, 100);

            Assert.Throws<InvalidOperationException>(() => lsb.EmbedBytes(new byte[] { 0x01 }, image));
        }

        [Fact]
        public void EmbedBytes_NullRowGenerator_ThrowsInvalidOperation()
        {
            var lsb = new Algorithms.LSB
            {
                ColumnSequenceGenerator = new Crypto.PRNG()
            };
            using var image = new Image<Rgba32>(100, 100);

            Assert.Throws<InvalidOperationException>(() => lsb.EmbedBytes(new byte[] { 0x01 }, image));
        }

        [Fact]
        public void ExtractBytes_NullGenerators_ThrowsInvalidOperation()
        {
            var lsb = new Algorithms.LSB();
            using var image = new Image<Rgba32>(100, 100);

            Assert.Throws<InvalidOperationException>(() => lsb.ExtractBytes(image));
        }

        [Fact]
        public void EmbedBytes_UninitializedPRNG_ThrowsInvalidOperation()
        {
            var lsb = new Algorithms.LSB
            {
                RowSequenceGenerator = new Crypto.PRNG(),
                ColumnSequenceGenerator = new Crypto.PRNG()
            };
            // PRNGs assigned but Initialize() never called
            using var image = new Image<Rgba32>(100, 100);

            Assert.Throws<InvalidOperationException>(() => lsb.EmbedBytes(new byte[] { 0x01 }, image));
        }

        [Fact]
        public void PRNG_InvalidName_ThrowsInvalidOperation()
        {
            var prng = new Crypto.PRNG();
            prng.Name = "NonExistent";

            Assert.Throws<InvalidOperationException>(() => prng.Initialize(42));
        }

        [Fact]
        public void EmbedBytes_ModifiesPixelsByAtMostOne()
        {
            byte[] data = new byte[] { 0xAA, 0x55 };

            using var original = new Image<Rgba32>(100, 100);
            for (int py = 0; py < 100; py++)
                for (int px = 0; px < 100; px++)
                    original[px, py] = new Rgba32(128, 128, 128, 255);

            using var image = original.Clone();
            var lsb = CreateLSB(42);
            lsb.EmbedBytes(data, image);

            for (int py = 0; py < 100; py++)
            {
                for (int px = 0; px < 100; px++)
                {
                    var orig = original[px, py];
                    var modified = image[px, py];
                    Assert.InRange(Math.Abs(orig.R - modified.R), 0, 1);
                    Assert.InRange(Math.Abs(orig.G - modified.G), 0, 1);
                    Assert.InRange(Math.Abs(orig.B - modified.B), 0, 1);
                }
            }
        }
    }
}
