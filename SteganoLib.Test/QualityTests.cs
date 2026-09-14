using System;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Audio;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;
using SteganoLib.Quality;
using SteganoLib.Selection;
using Xunit;

namespace SteganoLib.Test
{
    public class QualityTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 0x55 });

        private static byte[] Random(int length, int seed)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }

        private static Image<Rgba32> Noisy(Image<Rgba32> source, int amplitude, int seed)
        {
            var image = source.Clone();
            var random = new Random(seed);
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    var p = image[x, y];
                    byte Shift(byte v) => (byte)Math.Clamp(v + random.Next(-amplitude, amplitude + 1), 0, 255);
                    image[x, y] = new Rgba32(Shift(p.R), Shift(p.G), Shift(p.B), p.A);
                }
            }
            return image;
        }

        // ---------- image metrics ----------

        [Fact]
        public void Image_IdenticalImages_AreLossless()
        {
            using var image = JpegImageTests.TestPicture(40, 30, 1);
            var result = ImageMetrics.Compare(image, image);

            Assert.Equal(0, result.Mse);
            Assert.Equal(double.PositiveInfinity, result.Psnr);
            Assert.Equal(1.0, result.Ssim, 9);
            Assert.Equal(0, result.ChangedSamples);
            Assert.Equal(3L * 40 * 30, result.TotalSamples);
            Assert.Equal(0, result.ChangeRate);
            Assert.Equal(0, result.MaxAbsoluteError);
        }

        [Fact]
        public void Image_SingleChangedSample_HasExactMseAndPsnr()
        {
            using var cover = JpegImageTests.TestPicture(10, 10, 2);
            using var stego = cover.Clone();
            var p = stego[3, 4];
            stego[3, 4] = new Rgba32((byte)(p.R > 127 ? p.R - 5 : p.R + 5), p.G, p.B, p.A);

            var result = ImageMetrics.Compare(cover, stego);

            Assert.Equal(25.0 / 300, result.Mse, 12);
            Assert.Equal(10 * Math.Log10(255.0 * 255 / (25.0 / 300)), result.Psnr, 9);
            Assert.Equal(1, result.ChangedSamples);
            Assert.Equal(5, result.MaxAbsoluteError);
            Assert.True(result.Ssim > 0.99 && result.Ssim < 1.0, $"ssim {result.Ssim}");
        }

        [Fact]
        public void Image_Ssim_FallsWithNoise_AndIsSymmetric()
        {
            using var cover = JpegImageTests.TestPicture(96, 64, 3);
            using var light = Noisy(cover, 1, 4);
            using var heavy = Noisy(cover, 40, 5);

            double lightSsim = ImageMetrics.Ssim(cover, light);
            double heavySsim = ImageMetrics.Ssim(cover, heavy);

            Assert.True(lightSsim > 0.97, $"light {lightSsim}");
            Assert.True(heavySsim < 0.7, $"heavy {heavySsim}");
            Assert.True(heavySsim < lightSsim);
            Assert.Equal(ImageMetrics.Ssim(light, cover), lightSsim, 12);
            Assert.True(ImageMetrics.Psnr(cover, light) > ImageMetrics.Psnr(cover, heavy));
        }

        [Fact]
        public void Image_Ssim_HandlesFlatAndTinyImages()
        {
            using var flat = new Image<Rgba32>(32, 32, new Rgba32(100, 100, 100));
            Assert.Equal(1.0, ImageMetrics.Ssim(flat, flat), 9);

            using var tinyA = JpegImageTests.TestPicture(5, 5, 6);
            using var tinyB = Noisy(tinyA, 3, 7);
            double tiny = ImageMetrics.Ssim(tinyA, tinyB);
            Assert.InRange(tiny, -1.0, 1.0);
            Assert.Equal(1.0, ImageMetrics.Ssim(tinyA, tinyA), 9);

            // A brighter copy keeps structure but loses luminance similarity.
            using var brighter = new Image<Rgba32>(32, 32, new Rgba32(200, 200, 200));
            Assert.InRange(ImageMetrics.Ssim(flat, brighter), 0.0, 0.9);
        }

        [Fact]
        public void Image_ArgumentChecks()
        {
            using var a = new Image<Rgba32>(4, 4);
            using var b = new Image<Rgba32>(4, 5);
            Assert.Throws<ArgumentException>(() => ImageMetrics.Compare(a, b));
            Assert.Throws<ArgumentNullException>(() => ImageMetrics.Compare(a, null));
            Assert.Throws<ArgumentException>(() => ImageMetrics.Ssim(new double[3], new double[4], 2, 2));
            Assert.Equal(double.PositiveInfinity, ImageMetrics.PsnrFromMse(0));
            Assert.Equal(48.13, ImageMetrics.PsnrFromMse(1), 2);
        }

        // ---------- audio metrics ----------

        [Fact]
        public void Audio_Compare()
        {
            var cover = PcmAudioTests.Synthetic(4000, 2, 16);
            Assert.Equal(double.PositiveInfinity, AudioMetrics.Snr(cover, cover));

            var stego = cover.Clone();
            stego.Samples[10] += 1;
            stego.Samples[11] -= 3;
            var result = AudioMetrics.Compare(cover, stego);

            Assert.Equal(2, result.ChangedSamples);
            Assert.Equal(8000, result.TotalSamples);
            Assert.Equal(3, result.MaxAbsoluteError);
            Assert.Equal(2.0 / 8000, result.ChangeRate, 12);
            double signal = cover.Samples.Sum(s => (double)s * s);
            Assert.Equal(10 * Math.Log10(signal / 10), result.Snr, 9);
            Assert.Equal(10 * Math.Log10(32767.0 * 32767 * 8000 / 10), result.Psnr, 9);

            Assert.Throws<ArgumentException>(() => AudioMetrics.Compare(cover, PcmAudioTests.Synthetic(4000, 1, 16)));
        }

        // ---------- reports ----------

        [Fact]
        public void Report_Lsb_CountsChangesAndRate()
        {
            using var image = JpegImageTests.TestPicture(64, 48, 8);
            using var original = image.Clone();
            var lsb = new Algorithms.LSB(new KeyedPermutationSelector(Key));
            var data = Random(150, 9);

            var report = lsb.EmbedWithReport(data, image);

            Assert.Equal(150, report.PayloadBytes);
            Assert.Equal(378, report.Capacity);
            Assert.Equal(150.0 / 378, report.EmbeddingRate, 12);
            Assert.InRange(report.Distortion.ChangedSamples, 1, (150 + 6) * 8); // at most one sample per embedded bit
            Assert.Equal(1, report.Distortion.MaxAbsoluteError);
            Assert.True(report.Distortion.Psnr > 50, $"psnr {report.Distortion.Psnr}");
            Assert.True(report.Distortion.Ssim > 0.99, $"ssim {report.Distortion.Ssim}");
            Assert.Equal(ImageMetrics.Compare(original, image).Mse, report.Distortion.Mse);
            Assert.Equal(data, lsb.ExtractBytes(image));
        }

        [Fact]
        public void Report_Pipeline_UsesPayloadNotSealedLength()
        {
            using var image = JpegImageTests.TestPicture(64, 48, 10);
            var pipeline = new StegoPipeline<Image<Rgba32>>(new Algorithms.LSB(new KeyedPermutationSelector(Key)));
            var data = Random(100, 11);

            var report = pipeline.EmbedWithReport(data, image, Key);

            Assert.Equal(100, report.PayloadBytes);
            Assert.Equal(378 - pipeline.Envelope.Overhead, report.Capacity);
            Assert.True(report.Distortion.ChangedSamples > (100 + 6) * 8 / 4, "the envelope's overhead is embedded too");
            Assert.Equal(data, pipeline.Extract(image, Key).Data);
        }

        [Fact]
        public void Report_Audio()
        {
            var audio = PcmAudioTests.Synthetic(5000, 2, 16);
            var lsb = new AudioLsb(new KeyedSampleSelector(Key));
            var data = Random(200, 12);

            var report = lsb.EmbedWithReport(data, audio);

            Assert.Equal(200, report.PayloadBytes);
            Assert.Equal(lsb.Capacity(PcmAudioTests.Synthetic(5000, 2, 16)), report.Capacity);
            Assert.InRange(report.Distortion.ChangedSamples, 1, (200 + 6) * 8);
            Assert.True(report.Distortion.Snr > 70, $"snr {report.Distortion.Snr}");
            Assert.Equal(data, lsb.ExtractBytes(audio));
        }

        [Fact]
        public void Report_Jpeg_F5()
        {
            var image = JpegImage.Load(JpegImageTests.SampleJpeg(128, 96, seed: 13));
            var f5 = new F5(Key);
            var data = Random(60, 14);

            var report = f5.EmbedWithReport(data, image);

            Assert.Equal(60, report.PayloadBytes);
            Assert.True(report.Capacity >= 60);
            Assert.InRange(report.Distortion.ChangedCoefficients, 1, 60 * 8 + 48);
            Assert.Equal(1, report.Distortion.MaxAbsoluteChange);
            Assert.True(report.Distortion.NonZeroCoefficients > report.Distortion.ChangedCoefficients);
            Assert.InRange(report.Distortion.ChangeRate, 0.0, 1.0);
            Assert.True(report.Distortion.Pixels.Psnr > 35, $"psnr {report.Distortion.Pixels.Psnr}");
            Assert.Equal(data, new F5(Key).ExtractBytes(JpegImage.Load(image.ToArray())));
        }

        [Fact]
        public void Report_ArgumentChecks()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EmbeddingReport<int>(-1, 0, 1));
            Assert.Throws<ArgumentNullException>(() => new EmbeddingReport<string>(0, 0, null));
            Assert.Equal(0, new EmbeddingReport<int>(5, 0, 1).EmbeddingRate);
            Assert.Equal(1, new EmbeddingReport<int>(7, 5, 1).EmbeddingRate);

            using var image = new Image<Rgba32>(8, 8);
            var lsb = new Algorithms.LSB(new KeyedPermutationSelector(Key));
            Assert.Throws<ArgumentNullException>(() => lsb.EmbedWithReport(null, image));
            Assert.Throws<CapacityExceededException>(() => lsb.EmbedWithReport(new byte[100], image));
        }
    }
}
