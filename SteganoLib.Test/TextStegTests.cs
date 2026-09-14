using System;
using System.IO;
using System.Linq;
using SteganoLib.Algorithms;
using SteganoLib.Coding;
using SteganoLib.Crypto;
using SteganoLib.Payload;
using SteganoLib.Text;
using Xunit;

namespace SteganoLib.Test
{
    public class TextStegTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 0x7E });
        private static readonly StegoKey OtherKey = StegoKey.FromBytes(new byte[] { 0x7F });

        private static string Prose(int paragraphs = 30)
        {
            var random = new Random(paragraphs);
            string[] words = { "the", "quick", "brown", "fox", "jumps", "over", "a", "lazy", "dog", "while", "seven", "pipers", "play", "Cyrillic", "looks", "Latin", "at", "a", "glance", "so", "nobody", "checks" };
            var lines = Enumerable.Range(0, paragraphs).Select(_ =>
                string.Join(' ', Enumerable.Range(0, 12).Select(__ => words[random.Next(words.Length)])) + ".");
            return string.Join("\n", lines) + "\n";
        }

        private static byte[] Random(int length, int seed)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }

        // ---------- zero width ----------

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(200)]
        public void ZeroWidth_RoundTrip_AndVisibleTextUnchanged(int length)
        {
            string cover = Prose();
            var data = Random(length, length);

            string stego = new ZeroWidthCoding(Key).Embed(data, cover);

            Assert.Equal(data, new ZeroWidthCoding(Key).ExtractBytes(new TextCarrier(stego)));
            Assert.Equal(cover, ZeroWidthCoding.Strip(stego));
            Assert.Equal(cover.Length + (length + 4) * 8, stego.Length);
        }

        [Fact]
        public void ZeroWidth_Capacity_IsExact()
        {
            var carrier = new TextCarrier("one two three four five\nsix seven");
            var coder = new ZeroWidthCoding(Key) { BitsPerGap = 16 };

            // Gaps follow "one".."five" and "six": 6 gaps, 96 bits, 12 bytes, minus header.
            Assert.Equal(6 * 16 / 8 - 4, coder.Capacity(carrier));
            coder.EmbedBytes(Random(8, 1), carrier);
            Assert.Equal(Random(8, 1), coder.ExtractBytes(carrier));
            Assert.Throws<CapacityExceededException>(() => coder.EmbedBytes(new byte[9], new TextCarrier("one two three four five\nsix seven")));
        }

        [Fact]
        public void ZeroWidth_ExistingInvisibleCharactersAreRemoved()
        {
            string cover = "hel​lo wor‌ld again";
            var carrier = new TextCarrier(cover);

            new ZeroWidthCoding(Key) { BitsPerGap = 24 }.EmbedBytes(new byte[] { 0xAB }, carrier);

            Assert.Equal("hello world again", ZeroWidthCoding.Strip(carrier.Text));
            Assert.Equal(new byte[] { 0xAB }, new ZeroWidthCoding(Key) { BitsPerGap = 24 }.ExtractBytes(carrier));
        }

        [Fact]
        public void ZeroWidth_NoGaps_ZeroCapacity()
        {
            Assert.Equal(0, new ZeroWidthCoding(Key).Capacity(new TextCarrier("singleword")));
            Assert.Equal(0, new ZeroWidthCoding(Key).Capacity(new TextCarrier("")));
            Assert.Empty(new ZeroWidthCoding(Key).ExtractBytes(new TextCarrier("plain text with no marks")));
        }

        [Fact]
        public void ZeroWidth_WrongKey_DoesNotRecover()
        {
            var data = Random(50, 3);
            string stego = new ZeroWidthCoding(Key).Embed(data, Prose());

            Assert.NotEqual(data, new ZeroWidthCoding(OtherKey).ExtractBytes(new TextCarrier(stego)));
        }

        // ---------- whitespace ----------

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(20)]
        public void Whitespace_RoundTrip_AndTrimmedLinesUnchanged(int length)
        {
            string cover = Prose();
            var data = Random(length, length + 100);

            string stego = new WhitespaceCoding(Key).Embed(data, cover);

            Assert.Equal(data, new WhitespaceCoding(Key).ExtractBytes(new TextCarrier(stego)));
            Assert.Equal(cover.Split('\n').Select(l => l.TrimEnd()), stego.Split('\n').Select(l => l.TrimEnd()));
        }

        [Fact]
        public void Whitespace_Capacity_AndCrlf()
        {
            var carrier = new TextCarrier("line one\r\nline two\r\nline three\r\nlast line without newline");
            var coder = new WhitespaceCoding(Key) { BitsPerLine = 16 };

            Assert.Equal(4 * 16 / 8 - 4, coder.Capacity(carrier));
            coder.EmbedBytes(new byte[] { 1, 2, 3, 4 }, carrier);

            Assert.Contains("\r\n", carrier.Text);
            Assert.DoesNotContain("\n\r", carrier.Text);
            Assert.EndsWith("last line without newline", carrier.Text.TrimEnd(' ', '\t'));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, coder.ExtractBytes(carrier));
        }

        [Fact]
        public void Whitespace_ExistingTrailingWhitespaceIsReplaced()
        {
            var carrier = new TextCarrier("a  \t\nb \nc\t\nd\n");
            var coder = new WhitespaceCoding(Key) { BitsPerLine = 12 };

            coder.EmbedBytes(new byte[] { 0x5A }, carrier);

            Assert.Equal(new byte[] { 0x5A }, coder.ExtractBytes(carrier));
            Assert.Equal(new[] { "a", "b", "c", "d", "" }, carrier.Text.Split('\n').Select(l => l.TrimEnd(' ', '\t')));
        }

        [Fact]
        public void Whitespace_UnmarkedText_ExtractsNothing()
        {
            Assert.Empty(new WhitespaceCoding(Key).ExtractBytes(new TextCarrier(Prose())));
            Assert.Equal(0, new WhitespaceCoding(Key).Capacity(new TextCarrier("no newline here")));
        }

        // ---------- homoglyphs ----------

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(30)]
        public void Homoglyph_RoundTrip_LengthAndNormalizedTextUnchanged(int length)
        {
            string cover = Prose(80);
            var data = Random(length, length + 200);

            string stego = new HomoglyphCoding(Key).Embed(data, cover);

            Assert.Equal(data, new HomoglyphCoding(Key).ExtractBytes(new TextCarrier(stego)));
            Assert.Equal(cover.Length, stego.Length);
            Assert.Equal(cover, HomoglyphCoding.Normalize(stego));
            if (length > 0)
                Assert.NotEqual(cover, stego);
        }

        [Fact]
        public void Homoglyph_OnlyTableLettersChange()
        {
            string cover = Prose(60);
            string stego = new HomoglyphCoding(Key).Embed(Random(20, 9), cover);

            for (int i = 0; i < cover.Length; i++)
            {
                if (cover[i] == stego[i])
                    continue;
                Assert.True(HomoglyphCoding.IsSlot(cover[i]), $"'{cover[i]}' changed");
                Assert.True(HomoglyphCoding.IsSlot(stego[i]), $"'{stego[i]}' is not a twin");
                Assert.True(stego[i] > 0x400, $"'{stego[i]}' is not Cyrillic");
            }
        }

        [Fact]
        public void Homoglyph_Capacity_IsExact()
        {
            string cover = Prose(40);
            var coder = new HomoglyphCoding(Key);
            var carrier = new TextCarrier(cover);

            int slots = cover.Count(HomoglyphCoding.IsSlot);
            Assert.Equal(slots / 8 - 6, coder.Capacity(carrier));

            var data = Random((int)coder.Capacity(carrier), 4);
            coder.EmbedBytes(data, carrier);
            Assert.Equal(data, coder.ExtractBytes(carrier));
            Assert.Throws<CapacityExceededException>(() => coder.EmbedBytes(new byte[data.Length + 1], new TextCarrier(cover)));
        }

        [Fact]
        public void Homoglyph_Trellis_SwapsFewerLetters()
        {
            string cover = Prose(120);
            var data = Random(40, 5);

            string direct = new HomoglyphCoding(Key).Embed(data, cover);
            var trellis = new HomoglyphCoding(Key) { TrellisCoder = new SyndromeTrellisCoder(7) };
            string coded = trellis.Embed(data, cover);

            int directChanges = cover.Zip(direct).Count(p => p.First != p.Second);
            int trellisChanges = cover.Zip(coded).Count(p => p.First != p.Second);
            Assert.True(trellisChanges < directChanges / 2, $"trellis={trellisChanges} direct={directChanges}");
            Assert.Equal(data, new HomoglyphCoding(Key).ExtractBytes(new TextCarrier(coded)));
        }

        [Fact]
        public void Homoglyph_CoverWithCyrillicTwins_ReadsAsOnes()
        {
            // A cover that already contains twins still round-trips: slots are read, not assumed zero.
            string cover = "раssword " + Prose(30);
            var data = Random(10, 6);

            string stego = new HomoglyphCoding(Key).Embed(data, cover);

            Assert.Equal(data, new HomoglyphCoding(Key).ExtractBytes(new TextCarrier(stego)));
        }

        // ---------- pipeline and files ----------

        [Fact]
        public void Pipeline_AllThreeMethods()
        {
            var data = Random(30, 7);
            string cover = Prose(80);

            foreach (var algorithm in new IStegAlgorithm<TextCarrier>[] { new ZeroWidthCoding(Key), new WhitespaceCoding(Key), new HomoglyphCoding(Key) })
            {
                var pipeline = new StegoPipeline<TextCarrier>(algorithm);
                string stego = pipeline.Embed(data, cover, Key);

                var result = pipeline.ExtractFromText(stego, Key);
                Assert.Equal(ExtractionStatus.Success, result.Status);
                Assert.Equal(data, result.Data);
                Assert.Equal(ExtractionStatus.NotFound, pipeline.ExtractFromText(cover, Key).Status);
            }
        }

        [Fact]
        public void FileHelpers_RoundTrip()
        {
            var input = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
            var output = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
            try
            {
                new TextCarrier(Prose(40)).Save(input);
                var data = Random(30, 8);

                var pipeline = new StegoPipeline<TextCarrier>(new ZeroWidthCoding(Key));
                pipeline.Embed(data, input, output, Key);
                Assert.Equal(data, pipeline.Extract(output, Key).Data);

                new WhitespaceCoding(Key).EmbedBytes(data, input, output);
                Assert.Equal(data, new WhitespaceCoding(Key).ExtractBytes(output));

                using var stream = File.OpenRead(output);
                Assert.Equal(data, new WhitespaceCoding(Key).ExtractBytes(TextCarrier.Load(stream)));
                Assert.Equal(
                    TextCarrier.Load(input).Text.Split('\n').Select(l => l.TrimEnd()),
                    TextCarrier.Load(output).Text.Split('\n').Select(l => l.TrimEnd(' ', '\t')));
            }
            finally
            {
                File.Delete(input);
                File.Delete(output);
            }
        }

        [Fact]
        public void Validation()
        {
            Assert.Throws<ArgumentNullException>(() => new TextCarrier(null));
            Assert.Throws<ArgumentNullException>(() => new TextCarrier("x").Text = null);
            Assert.Throws<ArgumentNullException>(() => new ZeroWidthCoding(null));
            Assert.Throws<ArgumentNullException>(() => new WhitespaceCoding(null));
            Assert.Throws<ArgumentNullException>(() => new HomoglyphCoding(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ZeroWidthCoding(Key).BitsPerGap = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ZeroWidthCoding(Key).BitsPerGap = 65);
            Assert.Throws<ArgumentOutOfRangeException>(() => new WhitespaceCoding(Key).BitsPerLine = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => new HomoglyphCoding(Key).MaxTrellisWidth = 0);
            Assert.Throws<ArgumentNullException>(() => new ZeroWidthCoding(Key).EmbedBytes(null, new TextCarrier("a b")));
            Assert.Throws<ArgumentNullException>(() => new ZeroWidthCoding(Key).ExtractBytes(null));
        }
    }
}
