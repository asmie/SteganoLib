using System;
using System.IO;
using System.Linq;
using System.Text;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Coding;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;
using SteganoLib.Metadata;
using SteganoLib.Payload;
using SteganoLib.Quality;
using SteganoLib.Selection;
using SteganoLib.Sharing;
using SteganoLib.Steganalysis;
using SteganoLib.Text;

namespace SteganoLib.Samples
{
    /// <summary>
    /// Small end-to-end examples of the library. Each sample builds its own cover, writes
    /// the result next to it and prints what happened. Run with
    /// <c>dotnet run --project samples/SteganoLib.Samples -- [output-directory]</c>.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string directory = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "stegano-samples");
            Run(Console.Out, directory);
            return 0;
        }

        public static void Run(TextWriter output, string directory)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            Directory.CreateDirectory(directory);

            // One key for everything. In real use derive it from a passphrase the receiver
            // knows (StegoKey.FromPassphrase) or share the bytes of StegoKey.CreateRandom().
            var key = StegoKey.FromPassphrase("correct horse battery staple", iterations: 50_000);

            output.WriteLine($"Writing samples to {directory}");
            HideInPng(output, directory, key);
            HideInJpeg(output, directory, key);
            HideInText(output, directory, key);
            HideInMetadata(output, directory, key);
            SurviveDamage(output, directory, key);
            ShareAcrossImages(output, directory, key);
            DetectLsbReplacement(output, directory);
        }

        /// <summary>The everyday case: an authenticated message in a PNG with LSB matching, plus a quality report.</summary>
        private static void HideInPng(TextWriter output, string directory, StegoKey key)
        {
            string cover = Path.Combine(directory, "cover.png");
            string stego = Path.Combine(directory, "stego.png");
            using (var picture = Covers.Picture(320, 240, 1))
                picture.SaveAsPng(cover);

            // The pipeline seals the payload (AES-GCM) so extraction can tell "nothing here"
            // from "wrong key". The keyed selector decides the pixel order.
            var pipeline = new StegoPipeline<Image<Rgba32>>(new LSB(new KeyedPermutationSelector(key)));
            var message = Encoding.UTF8.GetBytes("Meet at the usual place, 21:00.");

            using (var image = Image.Load<Rgba32>(cover))
            {
                var report = pipeline.EmbedWithReport(message, image, key);
                image.SaveAsPng(stego);
                output.WriteLine($"PNG: hid {report.PayloadBytes} bytes at {report.EmbeddingRate:P1} of capacity; PSNR {report.Distortion.Psnr:F1} dB, SSIM {report.Distortion.Ssim:F5}");
            }

            var result = pipeline.Extract(stego, key);
            output.WriteLine($"PNG: extracted \"{Encoding.UTF8.GetString(result.Data)}\" ({result.Status})");
            output.WriteLine($"PNG: with the wrong key: {pipeline.Extract(stego, StegoKey.FromBytes(new byte[] { 1 })).Status}");
        }

        /// <summary>JPEG needs a transform-domain method; F5 changes quantised coefficients and never re-compresses.</summary>
        private static void HideInJpeg(TextWriter output, string directory, StegoKey key)
        {
            string cover = Path.Combine(directory, "cover.jpg");
            string stego = Path.Combine(directory, "stego.jpg");
            using (var picture = Covers.Picture(320, 240, 2))
                picture.SaveAsJpeg(cover, new JpegEncoder { Quality = 85 });

            var pipeline = new StegoPipeline<JpegImage>(new F5(key));
            var payload = Encoding.UTF8.GetBytes("F5 lives in the DCT domain.");

            var image = JpegImage.Load(cover);
            var report = pipeline.EmbedWithReport(payload, image, key);
            image.Save(stego);
            output.WriteLine($"JPEG: changed {report.Distortion.ChangedCoefficients} of {report.Distortion.NonZeroCoefficients} non-zero coefficients; decoded PSNR {report.Distortion.Pixels.Psnr:F1} dB");
            output.WriteLine($"JPEG: extracted \"{Encoding.UTF8.GetString(pipeline.Extract(stego, key).Data)}\"");
        }

        /// <summary>Text carriers: invisible characters, trailing whitespace or look-alike letters.</summary>
        private static void HideInText(TextWriter output, string directory, StegoKey key)
        {
            string cover = string.Join("\n", Enumerable.Repeat("The quick brown fox jumps over the lazy dog while seven pipers play.", 12)) + "\n";
            var pipeline = new StegoPipeline<TextCarrier>(new ZeroWidthCoding(key));

            string stego = pipeline.Embed(Encoding.UTF8.GetBytes("zw"), cover, key);
            File.WriteAllText(Path.Combine(directory, "stego.txt"), stego);

            output.WriteLine($"Text: {stego.Length - cover.Length} zero-width characters added, visible text unchanged: {ZeroWidthCoding.Strip(stego) == cover}");
            output.WriteLine($"Text: extracted \"{Encoding.UTF8.GetString(pipeline.ExtractFromText(stego, key).Data)}\"");
        }

        /// <summary>Metadata carriers leave pixels alone; fast, exact, but gone after re-encoding.</summary>
        private static void HideInMetadata(TextWriter output, string directory, StegoKey key)
        {
            string cover = Path.Combine(directory, "cover.png");
            string stego = Path.Combine(directory, "stego-metadata.png");
            var pipeline = new StegoPipeline<MetadataCarrier>(new MetadataCoding());
            var options = new MetadataOptions { PngChunkType = "tEXt", PngKeyword = "Comment" }; // looks like an ordinary comment

            pipeline.Embed(Encoding.UTF8.GetBytes("hidden in a text chunk"), cover, stego, key, options);

            output.WriteLine($"Metadata: extracted \"{Encoding.UTF8.GetString(pipeline.Extract(stego, key, options).Data)}\" from a PNG tEXt chunk; pixels identical: {ImageMetrics.Mse(Image.Load<Rgba32>(cover), Image.Load<Rgba32>(stego)) == 0}");
        }

        /// <summary>Reed-Solomon around the algorithm: a few corrupted bytes are repaired before the payload is authenticated.</summary>
        private static void SurviveDamage(TextWriter output, string directory, StegoKey key)
        {
            var cover = File.ReadAllBytes(Path.Combine(directory, "cover.png"));
            var coder = new ErrorCorrectedAlgorithm<MetadataCarrier>(new MetadataCoding(), new ReedSolomonCode(paritySymbols: 32));
            var pipeline = new StegoPipeline<MetadataCarrier>(coder);
            var payload = Encoding.UTF8.GetBytes("This survives 16 corrupted bytes per 255-byte block.");

            var stego = pipeline.Embed(payload, cover, key);

            // Damage ten bytes of the hidden chunk.
            var store = new PngMetadataStore(stego);
            var chunk = store.Chunks.Single(c => c.Type == PngMetadataStore.DefaultChunkType);
            var random = new Random(7);
            for (int i = 0; i < 10; i++)
                chunk.Data[random.Next(chunk.Data.Length)] ^= 0xFF;
            var damaged = store.ToArray();

            coder.TryExtractBytes(MetadataCarrier.Load(damaged), out _, out int corrected);
            var result = pipeline.ExtractFromBytes(damaged, key);
            output.WriteLine($"Error correction: {corrected} bytes repaired, payload {result.Status}: \"{Encoding.UTF8.GetString(result.Data)}\"");
        }

        /// <summary>Shamir sharing: three images, any two recover the payload, one alone reveals nothing.</summary>
        private static void ShareAcrossImages(TextWriter output, string directory, StegoKey key)
        {
            var covers = Enumerable.Range(0, 3).Select(i => Covers.Picture(160, 120, 10 + i)).ToList();
            try
            {
                var pipeline = new StegoPipeline<System.Collections.Generic.IReadOnlyList<Image<Rgba32>>>(new SharedCoding<Image<Rgba32>>(new LSB(new KeyedPermutationSelector(key)), threshold: 2));
                pipeline.Embed(Encoding.UTF8.GetBytes("split three ways"), covers, key);
                for (int i = 0; i < covers.Count; i++)
                    covers[i].SaveAsPng(Path.Combine(directory, $"share{i}.png"));

                var two = pipeline.Extract(new[] { covers[2], covers[0] }, key);
                var one = pipeline.Extract(new[] { covers[1] }, key);
                output.WriteLine($"Sharing: two of three images give \"{Encoding.UTF8.GetString(two.Data)}\"; one alone gives {one.Status}");
            }
            finally
            {
                foreach (var cover in covers) cover.Dispose();
            }
        }

        /// <summary>Steganalysis as a sanity check: replacement is caught, the library's default matching is not.</summary>
        private static void DetectLsbReplacement(TextWriter output, string directory)
        {
            var key = StegoKey.FromBytes(new byte[] { 42 });
            using var cover = Covers.Picture(256, 192, 20);
            using var replaced = cover.Clone();
            using var matched = cover.Clone();

            var replace = new LSB(new KeyedPermutationSelector(key)) { EmbeddingMode = LsbEmbeddingMode.Replace, Channels = ColorChannels.Red };
            var match = new LSB(new KeyedPermutationSelector(key)) { Channels = ColorChannels.Red };
            var payload = new byte[replace.Capacity(cover)];
            new Random(1).NextBytes(payload);
            replace.EmbedBytes(payload, replaced);
            match.EmbedBytes(payload, matched);
            replaced.SaveAsPng(Path.Combine(directory, "lsb-replace.png"));

            var chi = new ChiSquareAttack { Channels = ColorChannels.Red };
            var spa = new SamplePairAnalysis { Channels = ColorChannels.Red };
            output.WriteLine($"Steganalysis: chi-square probability cover {chi.Analyze(cover).EmbeddingProbability:F2}, replacement {chi.Analyze(replaced).EmbeddingProbability:F2}, matching {chi.Analyze(matched).EmbeddingProbability:F2}");
            output.WriteLine($"Steganalysis: sample-pair rate cover {spa.Analyze(cover).EstimatedEmbeddingRate:F2}, replacement {spa.Analyze(replaced).EstimatedEmbeddingRate:F2}, matching {spa.Analyze(matched).EstimatedEmbeddingRate:F2}");
        }

        /// <summary>A photo-like synthetic cover: smooth content, mild noise, a tone curve.</summary>
        internal static class Covers
        {
            public static Image<Rgba32> Picture(int width, int height, int seed)
            {
                var random = new Random(seed);
                var lut = new byte[256];
                for (int i = 0; i < 256; i++)
                    lut[i] = (byte)Math.Round(255 * Math.Pow(i / 255.0, 1.3));
                byte Tone(double v) => lut[(int)Math.Clamp(Math.Round(v + random.Next(-3, 4)), 0, 255)];

                var image = new Image<Rgba32>(width, height);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        double b = 128 + 55 * Math.Sin(x / 13.0 + seed) * Math.Cos(y / 17.0) + 35 * Math.Sin((x + 2 * y) / 29.0);
                        image[x, y] = new Rgba32(Tone(b), Tone(0.9 * b + 10), Tone(0.8 * b + 20), 255);
                    }
                }
                return image;
            }
        }
    }
}
