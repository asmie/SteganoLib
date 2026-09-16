#nullable enable

using System;
using System.Collections.Generic;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Payload;

namespace SteganoLib.Sharing
{
    /// <summary>File helpers for payloads shared across several image files. Output must be PNG, BMP or TIFF; all output formats are validated before writing. TIFF output stores RGB.</summary>
    public static class SharingExtensions
    {
        /// <exception cref="CapacityExceededException">The smallest image cannot hold its share.</exception>
        public static void EmbedBytes(this IStegAlgorithm<IReadOnlyList<Image<Rgba32>>> algorithm, byte[] data, IReadOnlyList<string> inputPaths, IReadOnlyList<string> outputPaths)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            ArgumentNullException.ThrowIfNull(data);
            ValidateOutputs(inputPaths, outputPaths);
            var images = Load(inputPaths, outputPaths);
            try
            {
                algorithm.EmbedBytes(data, images);
                Save(images, outputPaths);
            }
            finally
            {
                Dispose(images);
            }
        }

        public static byte[] ExtractBytes(this IStegAlgorithm<IReadOnlyList<Image<Rgba32>>> algorithm, IReadOnlyList<string> paths)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            var images = Load(paths, null);
            try
            {
                return algorithm.ExtractBytes(images);
            }
            finally
            {
                Dispose(images);
            }
        }

        /// <exception cref="CapacityExceededException">The smallest image cannot hold its share of the sealed payload.</exception>
        public static void Embed(this StegoPipeline<IReadOnlyList<Image<Rgba32>>> pipeline, byte[] data, IReadOnlyList<string> inputPaths, IReadOnlyList<string> outputPaths, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(key);
            ValidateOutputs(inputPaths, outputPaths);
            var images = Load(inputPaths, outputPaths);
            try
            {
                pipeline.Embed(data, images, key);
                Save(images, outputPaths);
            }
            finally
            {
                Dispose(images);
            }
        }

        public static ExtractResult Extract(this StegoPipeline<IReadOnlyList<Image<Rgba32>>> pipeline, IReadOnlyList<string> paths, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            ArgumentNullException.ThrowIfNull(key);
            var images = Load(paths, null);
            try
            {
                return pipeline.Extract(images, key);
            }
            finally
            {
                Dispose(images);
            }
        }

        private static List<Image<Rgba32>> Load(IReadOnlyList<string> inputPaths, IReadOnlyList<string>? outputPaths)
        {
            if (inputPaths == null) throw new ArgumentNullException(nameof(inputPaths));
            if (outputPaths != null && outputPaths.Count != inputPaths.Count)
                throw new ArgumentException("One output path per input path is required.", nameof(outputPaths));

            var images = new List<Image<Rgba32>>(inputPaths.Count);
            try
            {
                foreach (var path in inputPaths)
                    images.Add(Image.Load<Rgba32>(path ?? throw new ArgumentException("Paths must not be null.", nameof(inputPaths))));
            }
            catch
            {
                Dispose(images);
                throw;
            }
            return images;
        }

        private static void ValidateOutputs(IReadOnlyList<string> inputPaths, IReadOnlyList<string> outputPaths)
        {
            if (inputPaths == null) throw new ArgumentNullException(nameof(inputPaths));
            if (outputPaths == null) throw new ArgumentNullException(nameof(outputPaths));
            if (outputPaths.Count != inputPaths.Count)
                throw new ArgumentException("One output path per input path is required.", nameof(outputPaths));
            foreach (var path in outputPaths)
                LosslessImageOutput.EncoderForPath(path ?? throw new ArgumentException("Paths must not be null.", nameof(outputPaths)));
        }

        private static void Save(List<Image<Rgba32>> images, IReadOnlyList<string> outputPaths)
        {
            for (int i = 0; i < images.Count; i++)
                images[i].Save(outputPaths[i], LosslessImageOutput.EncoderForPath(outputPaths[i]));
        }

        private static void Dispose(List<Image<Rgba32>> images)
        {
            foreach (var image in images)
                image.Dispose();
        }
    }
}
