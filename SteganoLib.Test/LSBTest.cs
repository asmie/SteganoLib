using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Selection;
using Xunit;

namespace SteganoLib.Test
{
    public class LSBTest
    {
        private static Algorithms.LSB CreateLSB(int seed, ColorChannels channels = ColorChannels.All, int bitsPerPixel = 1)
        {
            return new Algorithms.LSB(new PrngPixelSelector(rowSeed: seed, columnSeed: seed + 1))
            {
                Channels = channels,
                BitsPerPixel = bitsPerPixel
            };
        }

        [Fact]
        public void IsPossibleToEmbed_SufficientCapacity_ReturnsTrue()
        {
            IStegAlgorithm<Image<Rgba32>> lsb = CreateLSB(42);
            using var image = new Image<Rgba32>(100, 100);
            Assert.True(lsb.IsPossibleToEmbed(10, image));
        }

        [Fact]
        public void IsPossibleToEmbed_InsufficientCapacity_ReturnsFalse()
        {
            IStegAlgorithm<Image<Rgba32>> lsb = CreateLSB(42);
            // 4 pixels total, far below the 6-byte header.
            using var image = new Image<Rgba32>(2, 2);
            Assert.False(lsb.IsPossibleToEmbed(1, image));
        }

        [Fact]
        public void IsPossibleToEmbed_ExactBoundary_ReturnsTrue()
        {
            IStegAlgorithm<Image<Rgba32>> lsb = CreateLSB(42);
            // 1 byte data: need (1+6)*8 = 56 pixels with M=1, C=3
            using var image = new Image<Rgba32>(8, 7); // exactly 56 pixels
            Assert.True(lsb.IsPossibleToEmbed(1, image));
            Assert.False(lsb.IsPossibleToEmbed(2, image));
        }

        [Fact]
        public void IsPossibleToEmbed_SingleChannel_ReducedCapacity()
        {
            IStegAlgorithm<Image<Rgba32>> lsb = CreateLSB(42, ColorChannels.Red);
            // C=1, M=1: each pixel contributes 1 bit, capacity = W*H bits
            using var image = new Image<Rgba32>(10, 10); // 100 pixels
            Assert.True(lsb.IsPossibleToEmbed(6, image));  // (6+6)*8 = 96 <= 100
            Assert.False(lsb.IsPossibleToEmbed(7, image)); // (7+6)*8 = 104 > 100
        }

        [Fact]
        public void Channels_None_Throws()
        {
            var lsb = CreateLSB(42);
            Assert.Throws<ArgumentException>(() => lsb.Channels = ColorChannels.None);
            Assert.Throws<ArgumentException>(() => lsb.Channels = (ColorChannels)8);
        }

        [Theory]
        [InlineData(ColorChannels.All, 1, 100, 100, 1244)]     // 10000 bits / 8 - 6
        [InlineData(ColorChannels.Red, 1, 10, 10, 6)]           // 100 bits / 8 - 6
        [InlineData(ColorChannels.All, 3, 40, 40, 594)]         // 4800 bits / 8 - 6
        [InlineData(ColorChannels.All, 2, 3, 1, 0)]             // 3 bits total
        [InlineData(ColorChannels.All, 2, 100, 1, 12)]          // 50 cycles * 3 bits = 150 bits
        [InlineData(ColorChannels.Red | ColorChannels.Blue, 2, 8, 8, 10)] // 64 pixels * 2 bits = 128 bits
        public void Capacity_MatchesFormula(ColorChannels channels, int bitsPerPixel, int width, int height, long expected)
        {
            var lsb = CreateLSB(1, channels, bitsPerPixel);
            using var image = new Image<Rgba32>(width, height);

            Assert.Equal(expected, lsb.Capacity(image));
        }

        [Theory]
        [InlineData(ColorChannels.All, 1)]
        [InlineData(ColorChannels.All, 2)]
        [InlineData(ColorChannels.All, 3)]
        [InlineData(ColorChannels.Red, 1)]
        [InlineData(ColorChannels.Green | ColorChannels.Blue, 1)]
        [InlineData(ColorChannels.Green | ColorChannels.Blue, 2)]
        public void Capacity_IsExactlyReachable(ColorChannels channels, int bitsPerPixel)
        {
            var key = Crypto.StegoKey.FromBytes(new byte[] { 9 });
            using var image = new Image<Rgba32>(37, 23);
            var lsb = new Algorithms.LSB(new KeyedPermutationSelector(key)) { Channels = channels, BitsPerPixel = bitsPerPixel };

            int capacity = (int)lsb.Capacity(image);
            var data = new byte[capacity];
            new Random(capacity).NextBytes(data);

            lsb.EmbedBytes(data, image);
            Assert.Equal(data, lsb.ExtractBytes(image));
            Assert.Throws<CapacityExceededException>(() => lsb.EmbedBytes(new byte[capacity + 1], image));
        }

        [Fact]
        public void EmbedBytes_ImageTooSmall_ThrowsCapacityExceeded()
        {
            var lsb = CreateLSB(42);
            using var image = new Image<Rgba32>(2, 2);

            var ex = Assert.Throws<CapacityExceededException>(() => lsb.EmbedBytes(new byte[] { 0x41, 0x42, 0x43 }, image));
            Assert.Equal(3, ex.Required);
            Assert.Equal(0, ex.Available);
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

            embedLsb.EmbedBytes(data, image);

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

            embedLsb.EmbedBytes(data, image);

            var extractLsb = CreateLSB(42);
            byte[] extracted = extractLsb.ExtractBytes(image);

            Assert.Equal(data, extracted);
        }

        [Fact]
        public void EmbedAndExtract_SingleChannel_RoundTrip()
        {
            byte[] data = new byte[] { 0x41, 0x42 };

            var embedLsb = CreateLSB(42, ColorChannels.Red);
            using var image = new Image<Rgba32>(100, 100);

            embedLsb.EmbedBytes(data, image);

            var extractLsb = CreateLSB(42, ColorChannels.Red);
            byte[] extracted = extractLsb.ExtractBytes(image);

            Assert.Equal(data, extracted);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void EmbedAndExtract_MaxBitsGreaterThanOne_RoundTrip(int maxBits)
        {
            byte[] data = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };

            var embedLsb = CreateLSB(42, bitsPerPixel: maxBits);
            using var image = new Image<Rgba32>(100, 100);

            embedLsb.EmbedBytes(data, image);

            var extractLsb = CreateLSB(42, bitsPerPixel: maxBits);
            byte[] extracted = extractLsb.ExtractBytes(image);

            Assert.Equal(data, extracted);
        }

        [Fact]
        public void EmbedAndExtract_MaxBitsGreaterThanChannels_RoundTrip()
        {
            byte[] data = new byte[] { 0xAA, 0x55 };

            // M=4 > C=3: channels exhaust before bit limit, so behaves like M=C
            var embedLsb = CreateLSB(42, bitsPerPixel: 4);
            using var image = new Image<Rgba32>(100, 100);

            embedLsb.EmbedBytes(data, image);

            var extractLsb = CreateLSB(42, bitsPerPixel: 4);
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
            embedLsb.EmbedBytes(new byte[] { 0x01, 0x02, 0x03 }, image);

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
            // Length 0x1FFFFFFC + header, times 8, overflows a 32-bit int to a small value.
            byte[] header = { 0, 0, 0x1F, 0xFF, 0xFF, 0xFC };
            const int seed = 7;

            using var image = new Image<Rgba32>(100, 100);
            var rowPrng = new Crypto.PRNG();
            rowPrng.Initialize(seed);
            var colPrng = new Crypto.PRNG();
            colPrng.Initialize(seed + 1);

            // Single channel, one bit per pixel: bit i lands in the i-th distinct pixel.
            var used = new HashSet<(int, int)>();
            for (int i = 0; i < header.Length * 8; i++)
            {
                int x, y;
                do
                {
                    x = colPrng.Next(image.Width);
                    y = rowPrng.Next(image.Height);
                } while (!used.Add((x, y)));

                bool bit = ((header[i / 8] >> (i % 8)) & 1) == 1;
                var px = image[x, y];
                px.R = (byte)(bit ? (px.R | 1) : (px.R & 254));
                image[x, y] = px;
            }

            var lsb = CreateLSB(seed, ColorChannels.Red);
            var result = lsb.ExtractBytes(image);

            Assert.Empty(result);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void BitsPerPixel_LessThanOne_Throws(int value)
        {
            var lsb = CreateLSB(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => lsb.BitsPerPixel = value);
        }

        [Fact]
        public void IsPossibleToEmbed_NegativeLength_ReturnsFalse()
        {
            IStegAlgorithm<Image<Rgba32>> lsb = CreateLSB(42);
            using var image = new Image<Rgba32>(100, 100);
            Assert.False(lsb.IsPossibleToEmbed(-1, image));
        }

        [Fact]
        public void IsPossibleToEmbed_HugeLength_DoesNotOverflow()
        {
            IStegAlgorithm<Image<Rgba32>> lsb = CreateLSB(42);
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

            // 64 * 64 pixels at one bit each hold 512 bytes including the 6-byte header.
            using var image = new Image<Rgba32>(64, 64);
            var writer = new Algorithms.LSB(new KeyedPermutationSelector(key));
            writer.EmbedBytes(data, image);

            var reader = new Algorithms.LSB(new KeyedPermutationSelector(key));
            Assert.Equal(data, reader.ExtractBytes(image));
        }

        [Fact]
        public void EmbedAndExtract_KeyedSelector_FullCapacity_RoundTrip()
        {
            var key = Crypto.StegoKey.FromBytes(new byte[] { 1, 2, 3 });
            using var image = new Image<Rgba32>(40, 40);
            var lsb = new Algorithms.LSB(new KeyedPermutationSelector(key)) { BitsPerPixel = 3 };

            // 1600 pixels * 3 bits = 4800 bits = 600 bytes including the 6-byte header.
            var data = new byte[594];
            new Random(9).NextBytes(data);

            Assert.Equal(594, lsb.Capacity(image));
            lsb.EmbedBytes(data, image);
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
            writer.EmbedBytes(data, image);

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
