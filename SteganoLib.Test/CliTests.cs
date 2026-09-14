using System;
using System.IO;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SteganoLib.Cli;
using Xunit;

namespace SteganoLib.Test
{
    public class CliTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "stegano-cli-" + Path.GetRandomFileName());

        public CliTests()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllBytes(KeyPath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
            File.WriteAllBytes(OtherKeyPath, new byte[] { 99, 98, 97 });
        }

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        private string KeyPath => Path.Combine(_dir, "key.bin");

        private string OtherKeyPath => Path.Combine(_dir, "other.bin");

        private string In(string name) => Path.Combine(_dir, name);

        private static (int Code, string Out, string Err) Run(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            int code = Program.Run(args, output, error);
            return (code, output.ToString(), error.ToString());
        }

        private string Png(string name = "cover.png", int width = 96, int height = 64)
        {
            using var picture = JpegImageTests.TestPicture(width, height, 800);
            string path = In(name);
            picture.SaveAsPng(path);
            return path;
        }

        private string Jpeg(string name = "cover.jpg")
        {
            using var picture = JpegImageTests.TestPicture(160, 120, 801);
            string path = In(name);
            File.WriteAllBytes(path, JpegImageTests.EncodeWithImageSharp(picture, JpegEncodingColor.YCbCrRatio420, 85));
            return path;
        }

        private string Wav(string name = "cover.wav")
        {
            string path = In(name);
            PcmAudioTests.Synthetic(6000, 2, 16).Save(path);
            return path;
        }

        private string Text(string name = "cover.txt")
        {
            string path = In(name);
            var sb = new StringBuilder();
            for (int i = 0; i < 80; i++)
                sb.AppendLine("the quick brown fox jumps over the lazy dog while seven pipers play");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        private string Payload(int length, int seed)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            string path = In($"payload{seed}.bin");
            File.WriteAllBytes(path, data);
            return path;
        }

        // ---------- round trips ----------

        [Theory]
        [InlineData("png", 200)]
        [InlineData("jpg", 120)]
        [InlineData("wav", 300)]
        [InlineData("txt", 60)]
        public void EmbedAndExtract_RoundTrip_AllCarriers(string kind, int length)
        {
            string cover = kind switch { "png" => Png(), "jpg" => Jpeg(), "wav" => Wav(), _ => Text() };
            string stego = In("stego." + kind);
            string payload = Payload(length, length);
            string recovered = In("recovered.bin");

            var embed = Run("embed", "-i", cover, "-o", stego, "-d", payload, "-k", KeyPath);
            Assert.Equal(0, embed.Code);
            Assert.Contains($"Embedded {length} bytes", embed.Out);
            Assert.Contains("Capacity", embed.Out);
            Assert.Empty(embed.Err);

            var extract = Run("extract", "-i", stego, "-o", recovered, "-k", KeyPath);
            Assert.Equal(0, extract.Code);
            Assert.Equal(File.ReadAllBytes(payload), File.ReadAllBytes(recovered));

            var wrong = Run("extract", "-i", stego, "-o", In("nothing.bin"), "-k", OtherKeyPath);
            Assert.Equal(1, wrong.Code);
            Assert.Contains("error:", wrong.Err);
            Assert.False(File.Exists(In("nothing.bin")));

            var clean = Run("extract", "-i", cover, "-k", KeyPath);
            Assert.Equal(1, clean.Code);
            Assert.Contains("No payload found", clean.Err);
        }

        [Fact]
        public void Message_AsText_AndPassphrase()
        {
            string stego = In("stego.png");

            var embed = Run("embed", "-i", Png(), "-o", stego, "-m", "hello from the cli", "--passphrase", "correct horse", "--compress");
            Assert.Equal(0, embed.Code);
            Assert.Contains("PSNR", embed.Out);
            Assert.Contains("SSIM", embed.Out);

            var extract = Run("extract", "-i", stego, "--as-text", "--passphrase", "correct horse");
            Assert.Equal(0, extract.Code);
            Assert.Equal("hello from the cli", extract.Out.Trim());

            var wrong = Run("extract", "-i", stego, "--as-text", "--passphrase", "battery staple");
            Assert.Equal(1, wrong.Code);
        }

        [Fact]
        public void Metadata_Carrier_AndErrorCorrection()
        {
            string stego = In("stego.png");
            string payload = Payload(500, 3);

            var embed = Run("embed", "-i", Png(), "-o", stego, "-d", payload, "-k", KeyPath, "--carrier", "metadata", "--ecc", "16");
            Assert.Equal(0, embed.Code);

            // The metadata payload is not in the pixels, so the default image extraction finds nothing.
            Assert.Equal(1, Run("extract", "-i", stego, "-k", KeyPath).Code);
            Assert.Equal(1, Run("extract", "-i", stego, "-k", KeyPath, "--carrier", "metadata").Code); // ecc setting must match

            var extract = Run("extract", "-i", stego, "-o", In("out.bin"), "-k", KeyPath, "-c", "metadata", "--ecc", "16");
            Assert.Equal(0, extract.Code);
            Assert.Equal(File.ReadAllBytes(payload), File.ReadAllBytes(In("out.bin")));
        }

        [Fact]
        public void Text_Methods()
        {
            foreach (string method in new[] { "zero-width", "whitespace", "homoglyph" })
            {
                string stego = In($"stego-{method}.txt");
                Assert.Equal(0, Run("embed", "-i", Text(), "-o", stego, "-m", "ok", "-k", KeyPath, "--text-method", method).Code);
                var extract = Run("extract", "-i", stego, "--as-text", "-k", KeyPath, "--text-method", method);
                Assert.Equal(0, extract.Code);
                Assert.Equal("ok", extract.Out.Trim());
            }
        }

        // ---------- other commands ----------

        [Fact]
        public void KeyGen()
        {
            var printed = Run("keygen");
            Assert.Equal(0, printed.Code);
            Assert.Equal(32, Convert.FromBase64String(printed.Out.Trim()).Length);

            var saved = Run("keygen", "-o", In("new.key"));
            Assert.Equal(0, saved.Code);
            Assert.Equal(32, File.ReadAllBytes(In("new.key")).Length);
            Assert.Equal(0, Run("embed", "-i", Png(), "-o", In("s.png"), "-m", "x", "-k", In("new.key")).Code);
        }

        [Fact]
        public void Capacity()
        {
            var result = Run("capacity", "-i", Png());
            Assert.Equal(0, result.Code);
            Assert.Contains("Carrier: Image", result.Out);
            Assert.Contains("Raw capacity: 762 bytes", result.Out); // 96*64/8 - 6
            Assert.Contains("Payload capacity:", result.Out);

            var ecc = Run("capacity", "-i", Png(), "--ecc", "32");
            Assert.Contains("error correction", ecc.Out);
            Assert.Equal(0, Run("capacity", "-i", Wav()).Code);
            Assert.Equal(0, Run("capacity", "-i", Jpeg()).Code);
            Assert.Equal(0, Run("capacity", "-i", Text(), "--text-method", "homoglyph").Code);
        }

        [Fact]
        public void Analyze_AndCompare()
        {
            string cover = Png("natural.png");
            using (var natural = SteganalysisTests.NaturalCover(128, 96))
                natural.SaveAsPng(cover);
            string stego = In("stego.png");
            Assert.Equal(0, Run("embed", "-i", cover, "-o", stego, "-m", "short", "-k", KeyPath).Code);

            var analyze = Run("analyze", "-i", cover);
            Assert.Equal(0, analyze.Code);
            Assert.Contains("Chi-square embedding probability", analyze.Out);
            Assert.Contains("RS estimated embedding rate", analyze.Out);
            Assert.Contains("Sample pair estimated embedding rate", analyze.Out);
            Assert.Contains("no sign of LSB replacement", analyze.Out);

            var compare = Run("compare", "--cover", cover, "--stego", stego);
            Assert.Equal(0, compare.Code);
            Assert.Contains("PSNR", compare.Out);
            Assert.Contains("samples changed", compare.Out);

            var same = Run("compare", "--cover", cover, "--stego", cover);
            Assert.Contains("infinite", same.Out);

            string wav = Wav();
            var audio = Run("compare", "--cover", wav, "--stego", wav);
            Assert.Equal(0, audio.Code);
            Assert.Contains("SNR infinite", audio.Out);
        }

        // ---------- errors ----------

        [Fact]
        public void UsageErrors_ExitWithTwo()
        {
            string cover = Png();

            Assert.Equal(2, Run("embed", "-i", cover, "-o", In("s.png"), "-m", "x").Code);                          // no key
            Assert.Equal(2, Run("embed", "-i", cover, "-o", In("s.png"), "-k", KeyPath).Code);                     // no payload
            Assert.Equal(2, Run("embed", "-i", cover, "-o", In("s.png"), "-m", "x", "-d", KeyPath, "-k", KeyPath).Code); // both payloads
            Assert.Equal(2, Run("embed", "-i", cover, "-m", "x", "-k", KeyPath).Code);                             // no output
            Assert.Equal(2, Run("embed", "-i", In("missing.png"), "-o", In("s.png"), "-m", "x", "-k", KeyPath).Code);
            Assert.Equal(2, Run("embed", "-i", cover, "-o", In("s.png"), "-m", "x", "-k", KeyPath, "-p", "p").Code);  // two keys
            Assert.Equal(2, Run("embed", "-i", cover, "-o", In("s.png"), "-m", "x", "-k", KeyPath, "--carrier", "video").Code);
            Assert.Equal(2, Run("embed", "-i", cover, "-o", In("s.png"), "-m", "x", "-k", KeyPath, "--ecc", "1").Code);
            File.WriteAllBytes(In("cover.dat"), new byte[10]);
            var unknown = Run("capacity", "-i", In("cover.dat"));
            Assert.Equal(2, unknown.Code);
            Assert.Contains("pass --carrier", unknown.Err);

            var tooBig = Run("embed", "-i", cover, "-o", In("s.png"), "-d", Payload(5000, 9), "-k", KeyPath);
            Assert.Equal(1, tooBig.Code);
            Assert.Contains("Payload too large", tooBig.Err);

            Assert.NotEqual(0, Run("extract").Code); // missing required --in is reported by the parser
            Assert.Equal(0, Run("--help").Code);
        }
    }
}
