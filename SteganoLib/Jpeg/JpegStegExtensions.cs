using System;
using System.IO;

using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Payload;

namespace SteganoLib.Jpeg
{
    /// <summary>File and stream helpers for JPEG carriers.</summary>
    public static class JpegStegExtensions
    {
        /// <exception cref="CapacityExceededException">The JPEG cannot hold <paramref name="data"/>.</exception>
        public static void EmbedBytes(this IStegAlgorithm<JpegImage> algorithm, byte[] data, string inputPath, string outputPath)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            var image = JpegImage.Load(inputPath);
            algorithm.EmbedBytes(data, image);
            image.Save(outputPath);
        }

        /// <exception cref="CapacityExceededException">The JPEG cannot hold <paramref name="data"/>.</exception>
        public static void EmbedBytes(this IStegAlgorithm<JpegImage> algorithm, byte[] data, Stream input, Stream output)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (output == null) throw new ArgumentNullException(nameof(output));

            var image = JpegImage.Load(input);
            algorithm.EmbedBytes(data, image);
            image.Save(output);
        }

        public static byte[] ExtractBytes(this IStegAlgorithm<JpegImage> algorithm, string path)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (path == null) throw new ArgumentNullException(nameof(path));

            return algorithm.ExtractBytes(JpegImage.Load(path));
        }

        public static byte[] ExtractBytes(this IStegAlgorithm<JpegImage> algorithm, Stream input)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (input == null) throw new ArgumentNullException(nameof(input));

            return algorithm.ExtractBytes(JpegImage.Load(input));
        }

        /// <exception cref="CapacityExceededException">The JPEG cannot hold the sealed payload.</exception>
        public static void Embed(this StegoPipeline<JpegImage> pipeline, byte[] data, string inputPath, string outputPath, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            var image = JpegImage.Load(inputPath);
            pipeline.Embed(data, image, key);
            image.Save(outputPath);
        }

        /// <exception cref="CapacityExceededException">The JPEG cannot hold the sealed payload.</exception>
        public static void Embed(this StegoPipeline<JpegImage> pipeline, byte[] data, Stream input, Stream output, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (output == null) throw new ArgumentNullException(nameof(output));

            var image = JpegImage.Load(input);
            pipeline.Embed(data, image, key);
            image.Save(output);
        }

        public static ExtractResult Extract(this StegoPipeline<JpegImage> pipeline, string path, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (path == null) throw new ArgumentNullException(nameof(path));

            return pipeline.Extract(JpegImage.Load(path), key);
        }

        public static ExtractResult Extract(this StegoPipeline<JpegImage> pipeline, Stream input, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (input == null) throw new ArgumentNullException(nameof(input));

            return pipeline.Extract(JpegImage.Load(input), key);
        }
    }
}
