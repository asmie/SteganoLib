using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Selection;
using Xunit;

namespace SteganoLib.Test
{
    public class LSBTest
    {
        private static Algorithms.LSB CreateLSB(int seed, bool modifyR = true, bool modifyG = true, bool modifyB = true, int maxBits = 1)
        {
            return new Algorithms.LSB(new PrngPixelSelector(rowSeed: seed, columnSeed: seed + 1))
            {
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
        public void Constructor_NullSelector_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => new Algorithms.LSB(null));
        }

        [Fact]
        public void PrngSelector_InvalidName_ThrowsInvalidOperation()
        {
            var lsb = new Algorithms.LSB(new PrngPixelSelector(1, 2, "NonExistent"));
            using var image = new Image<Rgba32>(100, 100);

            Assert.Throws<InvalidOperationException>(() => lsb.EmbedBytes(new byte[] { 1 }, image));
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

        [Fact]
        public void ExtractBytes_WrongSeed_DoesNotThrow()
        {
            var embedLsb = CreateLSB(42);
            using var image = new Image<Rgba32>(100, 100);
            Assert.True(embedLsb.EmbedBytes(new byte[] { 0x01, 0x02, 0x03 }, image));

            for (int seed = 100; seed < 300; seed++)
            {
                var extractLsb = CreateLSB(seed);
                var result = extractLsb.ExtractBytes(image);
                Assert.NotNull(result);
            }
        }

        [Fact]
        public void ExtractBytes_HeaderThatOverflowsInt32_ReturnsEmpty()
        {
            // 0x1FFFFFFC + 4 = 0x20000000; times 8 overflows a 32-bit int to zero.
            const uint header = 0x1FFFFFFC;
            const int seed = 7;

            using var image = new Image<Rgba32>(100, 100);
            var rowPrng = new Crypto.PRNG();
            rowPrng.Initialize(seed);
            var colPrng = new Crypto.PRNG();
            colPrng.Initialize(seed + 1);

            // Single channel, one bit per pixel: bit i lands in the i-th distinct pixel.
            var used = new HashSet<(int, int)>();
            for (int i = 0; i < 32; i++)
            {
                int x, y;
                do
                {
                    x = colPrng.Next(image.Width);
                    y = rowPrng.Next(image.Height);
                } while (!used.Add((x, y)));

                int byteIdx = i / 8;
                int bitInByte = i % 8;
                byte headerByte = (byte)(header >> (8 * (3 - byteIdx)));
                bool bit = ((headerByte >> bitInByte) & 1) == 1;

                var px = image[x, y];
                px.R = (byte)(bit ? (px.R | 1) : (px.R & 254));
                image[x, y] = px;
            }

            var lsb = CreateLSB(seed, modifyG: false, modifyB: false);
            var result = lsb.ExtractBytes(image);

            Assert.Empty(result);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ModifyMaxBitsInByte_LessThanOne_Throws(int value)
        {
            var lsb = CreateLSB(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => lsb.ModifyMaxBitsInByte = value);
        }

        [Fact]
        public void IsPossibleToEmbed_NegativeLength_ReturnsFalse()
        {
            var lsb = CreateLSB(42);
            using var image = new Image<Rgba32>(100, 100);
            Assert.False(lsb.IsPossibleToEmbed(-1, image));
        }

        [Fact]
        public void IsPossibleToEmbed_HugeLength_DoesNotOverflow()
        {
            var lsb = CreateLSB(42);
            using var image = new Image<Rgba32>(100, 100);
            Assert.False(lsb.IsPossibleToEmbed(int.MaxValue, image));
            Assert.False(lsb.IsPossibleToEmbed(0x1FFFFFFC, image));
        }

        [Fact]
        public void EmbedBytes_NullData_ThrowsArgumentNull()
        {
            var lsb = CreateLSB(42);
            using var image = new Image<Rgba32>(100, 100);
            Assert.Throws<ArgumentNullException>(() => lsb.EmbedBytes(null, image));
        }

        [Fact]
        public void EmbedBytes_NullImage_ThrowsArgumentNull()
        {
            var lsb = CreateLSB(42);
            Assert.Throws<ArgumentNullException>(() => lsb.EmbedBytes(new byte[] { 1 }, null));
        }

        [Fact]
        public void EmbedAndExtract_KeyedSelector_RoundTrip()
        {
            var key = Crypto.StegoKey.FromPassphrase("correct horse", iterations: 1000);
            var data = new byte[500];
            new Random(5).NextBytes(data);

            // 64 * 64 pixels at one bit each hold 512 bytes including the header.
            using var image = new Image<Rgba32>(64, 64);
            var writer = new Algorithms.LSB(new KeyedPermutationSelector(key));
            Assert.True(writer.EmbedBytes(data, image));

            var reader = new Algorithms.LSB(new KeyedPermutationSelector(key));
            Assert.Equal(data, reader.ExtractBytes(image));
        }

        [Fact]
        public void EmbedAndExtract_KeyedSelector_FullCapacity_RoundTrip()
        {
            var key = Crypto.StegoKey.FromBytes(new byte[] { 1, 2, 3 });
            using var image = new Image<Rgba32>(40, 40);
            var lsb = new Algorithms.LSB(new KeyedPermutationSelector(key)) { ModifyMaxBitsInByte = 3 };

            // 1600 pixels * 3 bits = 4800 bits = 600 bytes including the 4-byte header.
            var data = new byte[596];
            new Random(9).NextBytes(data);

            Assert.True(lsb.IsPossibleToEmbed(596, image));
            Assert.False(lsb.IsPossibleToEmbed(597, image));
            Assert.True(lsb.EmbedBytes(data, image));
            Assert.Equal(data, lsb.ExtractBytes(image));
        }

        [Fact]
        public void ExtractBytes_KeyedSelector_WrongKey_ReturnsEmptyOrGarbage()
        {
            var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            using var image = new Image<Rgba32>(64, 64);
            new Algorithms.LSB(new KeyedPermutationSelector(Crypto.StegoKey.FromBytes(new byte[] { 1 }))).EmbedBytes(data, image);

            var reader = new Algorithms.LSB(new KeyedPermutationSelector(Crypto.StegoKey.FromBytes(new byte[] { 2 })));
            var result = reader.ExtractBytes(image);

            Assert.NotEqual(data, result);
        }

        [Theory]
        [InlineData(Algorithms.LsbEmbeddingMode.Replace)]
        [InlineData(Algorithms.LsbEmbeddingMode.Match)]
        public void EmbedAndExtract_BothModes_RoundTrip(Algorithms.LsbEmbeddingMode mode)
        {
            var data = new byte[200];
            new Random(11).NextBytes(data);

            using var image = new Image<Rgba32>(100, 100);
            var writer = CreateLSB(3);
            writer.EmbeddingMode = mode;
            Assert.True(writer.EmbedBytes(data, image));

            Assert.Equal(data, CreateLSB(3).ExtractBytes(image));
        }

        [Fact]
        public void EmbeddingMode_DefaultsToMatch()
        {
            Assert.Equal(Algorithms.LsbEmbeddingMode.Match, CreateLSB(1).EmbeddingMode);
        }

        [Fact]
        public void Match_ChangesValuesInBothDirections()
        {
            var data = new byte[600];
            new Random(2).NextBytes(data);

            using var image = new Image<Rgba32>(100, 100);
            for (int py = 0; py < 100; py++)
                for (int px = 0; px < 100; px++)
                    image[px, py] = new Rgba32(100, 100, 100, 255);

            CreateLSB(4).EmbedBytes(data, image);

            int up = 0, down = 0;
            for (int py = 0; py < 100; py++)
            {
                for (int px = 0; px < 100; px++)
                {
                    var p = image[px, py];
                    foreach (var v in new[] { p.R, p.G, p.B })
                    {
                        if (v == 101) up++;
                        else if (v == 99) down++;
                        else Assert.Equal(100, v);
                    }
                }
            }

            // Replacement would only ever produce 101 from an even value.
            Assert.True(up > 100, $"up={up}");
            Assert.True(down > 100, $"down={down}");
        }

        [Fact]
        public void Replace_OnlySetsTheLowBit()
        {
            var data = new byte[600];
            new Random(2).NextBytes(data);

            using var image = new Image<Rgba32>(100, 100);
            for (int py = 0; py < 100; py++)
                for (int px = 0; px < 100; px++)
                    image[px, py] = new Rgba32(100, 100, 100, 255);

            var lsb = CreateLSB(4);
            lsb.EmbeddingMode = Algorithms.LsbEmbeddingMode.Replace;
            lsb.EmbedBytes(data, image);

            for (int py = 0; py < 100; py++)
                for (int px = 0; px < 100; px++)
                {
                    var p = image[px, py];
                    Assert.InRange(p.R, 100, 101);
                    Assert.InRange(p.G, 100, 101);
                    Assert.InRange(p.B, 100, 101);
                }
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(255, 254)]
        public void Match_StaysInsideByteRange(byte start, byte expected)
        {
            using var image = new Image<Rgba32>(100, 100);
            for (int py = 0; py < 100; py++)
                for (int px = 0; px < 100; px++)
                    image[px, py] = new Rgba32(start, start, start, 255);

            var data = new byte[600];
            new Random(8).NextBytes(data);
            CreateLSB(5).EmbedBytes(data, image);

            for (int py = 0; py < 100; py++)
                for (int px = 0; px < 100; px++)
                {
                    var p = image[px, py];
                    Assert.True(p.R == start || p.R == expected);
                    Assert.True(p.G == start || p.G == expected);
                    Assert.True(p.B == start || p.B == expected);
                }
        }
    }
}
