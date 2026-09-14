using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Audio;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;
using SteganoLib.Selection;
using SteganoLib.Text;
using Xunit;

namespace SteganoLib.Test
{
    /// <summary>
    /// Carriers too small for even the header report a capacity of zero. Embedding an
    /// empty payload into them must succeed without touching the carrier, so that
    /// <c>IsPossibleToEmbed(0)</c> and <c>EmbedBytes</c> agree, and extraction returns nothing.
    /// </summary>
    public class ZeroCapacityTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 0x77 });

        [Fact]
        public void Homoglyph_TextWithFewerSlotsThanHeader()
        {
            const string text = "the and fox\nfox pipers looks\ndog a Cyrillic\njumps fox looks\nseven the lazy\nplay fox Latin\nlazy fox\n";
            var coder = new HomoglyphCoding(Key);
            var carrier = new TextCarrier(text);

            Assert.Equal(0, coder.Capacity(carrier));
            Assert.True(((IStegAlgorithm<TextCarrier>)coder).IsPossibleToEmbed(0, carrier));
            coder.EmbedBytes(Array.Empty<byte>(), carrier);
            Assert.Equal(text, carrier.Text);
            Assert.Empty(coder.ExtractBytes(carrier));
            Assert.Throws<CapacityExceededException>(() => coder.EmbedBytes(new byte[1], carrier));
        }

        [Fact]
        public void Lsb_ImageWithFewerSlotsThanHeader()
        {
            using var image = JpegImageTests.TestPicture(5, 5, 1);
            using var cover = image.Clone();
            var lsb = new Algorithms.LSB(new KeyedPermutationSelector(Key));

            Assert.Equal(0, lsb.Capacity(image));
            lsb.EmbedBytes(Array.Empty<byte>(), image);
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 5; x++)
                    Assert.Equal(cover[x, y], image[x, y]);
            Assert.Empty(lsb.ExtractBytes(image));
            Assert.Throws<CapacityExceededException>(() => lsb.EmbedBytes(new byte[1], image));
        }

        [Fact]
        public void ZeroWidthAndWhitespace_TextWithNoRoom()
        {
            var zeroWidth = new ZeroWidthCoding(Key);
            var single = new TextCarrier("singleword");
            Assert.Equal(0, zeroWidth.Capacity(single));
            zeroWidth.EmbedBytes(Array.Empty<byte>(), single);
            Assert.Empty(zeroWidth.ExtractBytes(single));
            Assert.Throws<CapacityExceededException>(() => zeroWidth.EmbedBytes(new byte[1], single));

            var whitespace = new WhitespaceCoding(Key);
            var oneLine = new TextCarrier("one line only\n");
            Assert.True(whitespace.Capacity(oneLine) >= 0);
            whitespace.EmbedBytes(Array.Empty<byte>(), oneLine);
            Assert.Empty(whitespace.ExtractBytes(oneLine));
        }

        [Fact]
        public void Audio_TinyRecordings()
        {
            var lsb = new AudioLsb(new KeyedSampleSelector(Key));
            var tiny = new PcmAudio(8000, 1, 16, new int[10]);
            Assert.Equal(0, lsb.Capacity(tiny));
            lsb.EmbedBytes(Array.Empty<byte>(), tiny);
            Assert.Empty(lsb.ExtractBytes(tiny));
            Assert.Throws<CapacityExceededException>(() => lsb.EmbedBytes(new byte[1], tiny));

            var phase = new PhaseCoding(Key);
            var shortClip = PcmAudioTests.Synthetic(100, 1, 16);
            Assert.Equal(0, phase.Capacity(shortClip));
            phase.EmbedBytes(Array.Empty<byte>(), shortClip);
            Assert.Empty(phase.ExtractBytes(shortClip));
        }

        [Fact]
        public void F5_TinyJpeg()
        {
            var f5 = new F5(Key);
            var image = JpegImage.Load(JpegImageTests.SampleJpeg(8, 8, quality: 10));

            Assert.Equal(0, f5.Capacity(image));
            f5.EmbedBytes(Array.Empty<byte>(), image);
            Assert.Empty(f5.ExtractBytes(image));
            Assert.Throws<CapacityExceededException>(() => f5.EmbedBytes(new byte[1], image));
        }
    }
}
