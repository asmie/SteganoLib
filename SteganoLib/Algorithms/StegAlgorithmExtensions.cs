using System;
using System.IO;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Crypto;
using SteganoLib.Payload;

namespace SteganoLib.Algorithms
{
    /// <summary>
    /// File and stream helpers so callers do not have to load and save the image themselves.
    /// Output written by path takes its format from the extension; it must be lossless
    /// (PNG, BMP). Stream output is always PNG.
    /// </summary>
    public static class StegAlgorithmExtensions
    {
        /// <exception cref="CapacityExceededException">The image cannot hold <paramref name="data"/>.</exception>
        public static void EmbedBytes(this IStegAlgorithm<Image<Rgba32>> algorithm, byte[] data, string inputPath, string outputPath)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            using var image = Image.Load<Rgba32>(inputPath);
            algorithm.EmbedBytes(data, image);
            image.Save(outputPath);
        }

        /// <exception cref="CapacityExceededException">The image cannot hold <paramref name="data"/>.</exception>
        public static void EmbedBytes(this IStegAlgorithm<Image<Rgba32>> algorithm, byte[] data, Stream input, Stream output)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (output == null) throw new ArgumentNullException(nameof(output));

            using var image = Image.Load<Rgba32>(input);
            algorithm.EmbedBytes(data, image);
            image.SaveAsPng(output);
        }

        public static byte[] ExtractBytes(this IStegAlgorithm<Image<Rgba32>> algorithm, string path)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (path == null) throw new ArgumentNullException(nameof(path));

            using var image = Image.Load<Rgba32>(path);
            return algorithm.ExtractBytes(image);
        }

        public static byte[] ExtractBytes(this IStegAlgorithm<Image<Rgba32>> algorithm, Stream input)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (input == null) throw new ArgumentNullException(nameof(input));

            using var image = Image.Load<Rgba32>(input);
            return algorithm.ExtractBytes(image);
        }

        /// <exception cref="CapacityExceededException">The image cannot hold the sealed payload.</exception>
        public static void Embed(this StegoPipeline<Image<Rgba32>> pipeline, byte[] data, string inputPath, string outputPath, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            using var image = Image.Load<Rgba32>(inputPath);
            pipeline.Embed(data, image, key);
            image.Save(outputPath);
        }

        /// <exception cref="CapacityExceededException">The image cannot hold the sealed payload.</exception>
        public static void Embed(this StegoPipeline<Image<Rgba32>> pipeline, byte[] data, Stream input, Stream output, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (output == null) throw new ArgumentNullException(nameof(output));

            using var image = Image.Load<Rgba32>(input);
            pipeline.Embed(data, image, key);
            image.SaveAsPng(output);
        }

        public static ExtractResult Extract(this StegoPipeline<Image<Rgba32>> pipeline, string path, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (path == null) throw new ArgumentNullException(nameof(path));

            using var image = Image.Load<Rgba32>(path);
            return pipeline.Extract(image, key);
        }

        public static ExtractResult Extract(this StegoPipeline<Image<Rgba32>> pipeline, Stream input, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (input == null) throw new ArgumentNullException(nameof(input));

            using var image = Image.Load<Rgba32>(input);
            return pipeline.Extract(image, key);
        }
    }
}
