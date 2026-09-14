using System;
using System.IO;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Audio;

namespace SteganoLib.Benchmarks
{
    /// <summary>Deterministic synthetic covers so runs are comparable.</summary>
    internal static class Covers
    {
        public static Image<Rgba32> Picture(int width, int height, int seed = 1)
        {
            var random = new Random(seed);
            var image = new Image<Rgba32>(width, height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double b = 128 + 55 * Math.Sin(x / 13.0) * Math.Cos(y / 17.0) + 35 * Math.Sin((x + 2 * y) / 29.0);
                    byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v + random.Next(-6, 7)), 0, 255);
                    image[x, y] = new Rgba32(Clamp(b), Clamp(0.9 * b + 10), Clamp(0.8 * b + 20), 255);
                }
            }
            return image;
        }

        public static byte[] Jpeg(int width, int height, int quality = 85)
        {
            using var picture = Picture(width, height, 2);
            using var stream = new MemoryStream();
            picture.SaveAsJpeg(stream, new JpegEncoder { Quality = quality });
            return stream.ToArray();
        }

        public static PcmAudio Audio(int frames, int channels = 2)
        {
            var random = new Random(3);
            var samples = new int[frames * channels];
            for (int f = 0; f < frames; f++)
            {
                double t = f / 44100.0;
                double tone = Math.Sin(2 * Math.PI * 440 * t) * 0.5 + Math.Sin(2 * Math.PI * 1234.5 * t) * 0.3;
                for (int c = 0; c < channels; c++)
                    samples[f * channels + c] = (int)Math.Round((tone + (random.NextDouble() - 0.5) * 0.2) * 32767 * 0.6);
            }
            return new PcmAudio(44100, channels, 16, samples);
        }

        public static byte[] Payload(int length, int seed = 4)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }
    }
}
