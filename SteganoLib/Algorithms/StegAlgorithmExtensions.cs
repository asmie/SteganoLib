using System;
using System.IO;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SteganoLib.Algorithms
{
    /// <summary>
    /// Convenience helpers that load/save the image so callers don't need to wire
    /// up <see cref="Image{TPixel}"/> directly.
    /// </summary>
    public static class StegAlgorithmExtensions
    {
        /// <summary>
        /// Load the image at <paramref name="inputPath"/>, embed <paramref name="data"/>,
        /// and save the result to <paramref name="outputPath"/>. The output format is
        /// inferred from the file extension and must be lossless (e.g. PNG, BMP).
        /// Saving to a lossy format (JPEG) will destroy the embedded payload.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the image lacks capacity for <paramref name="data"/>.</exception>
        public static void EmbedBytes(this IStegAlgorithm algorithm, byte[] data, string inputPath, string outputPath)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            using var image = Image.Load<Rgba32>(inputPath);
            if (!algorithm.EmbedBytes(data, image))
                throw new InvalidOperationException("Image does not have enough capacity for the payload.");
            image.Save(outputPath);
        }

        /// <summary>
        /// Load an image from <paramref name="input"/>, embed <paramref name="data"/>,
        /// and write the result to <paramref name="output"/> as PNG (lossless).
        /// </summary>
        /// <exception cref="InvalidOperationException">If the image lacks capacity for <paramref name="data"/>.</exception>
        public static void EmbedBytes(this IStegAlgorithm algorithm, byte[] data, Stream input, Stream output)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (output == null) throw new ArgumentNullException(nameof(output));

            using var image = Image.Load<Rgba32>(input);
            if (!algorithm.EmbedBytes(data, image))
                throw new InvalidOperationException("Image does not have enough capacity for the payload.");
            image.SaveAsPng(output);
        }

        /// <summary>
        /// Load the image at <paramref name="path"/> and extract a previously embedded payload.
        /// </summary>
        public static byte[] ExtractBytes(this IStegAlgorithm algorithm, string path)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (path == null) throw new ArgumentNullException(nameof(path));

            using var image = Image.Load<Rgba32>(path);
            return algorithm.ExtractBytes(image);
        }

        /// <summary>
        /// Load an image from <paramref name="input"/> and extract a previously embedded payload.
        /// </summary>
        public static byte[] ExtractBytes(this IStegAlgorithm algorithm, Stream input)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (input == null) throw new ArgumentNullException(nameof(input));

            using var image = Image.Load<Rgba32>(input);
            return algorithm.ExtractBytes(image);
        }
    }
}
