using System;
using System.IO;

using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Payload;

namespace SteganoLib.Text
{
    /// <summary>File and stream helpers for text carriers.</summary>
    public static class TextStegExtensions
    {
        /// <exception cref="CapacityExceededException">The text cannot hold <paramref name="data"/>.</exception>
        public static void EmbedBytes(this IStegAlgorithm<TextCarrier> algorithm, byte[] data, string inputPath, string outputPath)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            var carrier = TextCarrier.Load(inputPath);
            algorithm.EmbedBytes(data, carrier);
            carrier.Save(outputPath);
        }

        /// <summary>Embed into <paramref name="text"/> and return the result.</summary>
        public static string Embed(this IStegAlgorithm<TextCarrier> algorithm, byte[] data, string text)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));

            var carrier = new TextCarrier(text);
            algorithm.EmbedBytes(data, carrier);
            return carrier.Text;
        }

        public static byte[] ExtractBytes(this IStegAlgorithm<TextCarrier> algorithm, string path)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (path == null) throw new ArgumentNullException(nameof(path));

            return algorithm.ExtractBytes(TextCarrier.Load(path));
        }

        /// <exception cref="CapacityExceededException">The text cannot hold the sealed payload.</exception>
        public static void Embed(this StegoPipeline<TextCarrier> pipeline, byte[] data, string inputPath, string outputPath, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            var carrier = TextCarrier.Load(inputPath);
            pipeline.Embed(data, carrier, key);
            carrier.Save(outputPath);
        }

        /// <summary>Seal and embed into <paramref name="text"/> and return the result.</summary>
        public static string Embed(this StegoPipeline<TextCarrier> pipeline, byte[] data, string text, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));

            var carrier = new TextCarrier(text);
            pipeline.Embed(data, carrier, key);
            return carrier.Text;
        }

        public static ExtractResult Extract(this StegoPipeline<TextCarrier> pipeline, string path, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (path == null) throw new ArgumentNullException(nameof(path));

            return pipeline.Extract(TextCarrier.Load(path), key);
        }

        /// <summary>Extract from text held in memory.</summary>
        public static ExtractResult ExtractFromText(this StegoPipeline<TextCarrier> pipeline, string text, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));

            return pipeline.Extract(new TextCarrier(text), key);
        }
    }
}
