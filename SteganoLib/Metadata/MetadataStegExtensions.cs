#nullable enable

using System;
using System.IO;

using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Payload;

namespace SteganoLib.Metadata
{
    /// <summary>File, stream and in-memory helpers for metadata carriers. The output keeps the input's format.</summary>
    public static class MetadataStegExtensions
    {
        /// <exception cref="CapacityExceededException">The container cannot hold <paramref name="data"/>.</exception>
        public static void EmbedBytes(this IStegAlgorithm<MetadataCarrier> algorithm, byte[] data, string inputPath, string outputPath, MetadataOptions? options = null)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            ArgumentNullException.ThrowIfNull(data);
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            var carrier = MetadataCarrier.Load(inputPath, options);
            algorithm.EmbedBytes(data, carrier);
            carrier.Save(outputPath);
        }

        /// <exception cref="CapacityExceededException">The container cannot hold <paramref name="data"/>.</exception>
        public static void EmbedBytes(this IStegAlgorithm<MetadataCarrier> algorithm, byte[] data, Stream input, Stream output, MetadataOptions? options = null)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            ArgumentNullException.ThrowIfNull(data);
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (output == null) throw new ArgumentNullException(nameof(output));

            var carrier = MetadataCarrier.Load(input, options);
            algorithm.EmbedBytes(data, carrier);
            carrier.Save(output);
        }

        /// <summary>Embed into the container bytes and return the new file.</summary>
        public static byte[] Embed(this IStegAlgorithm<MetadataCarrier> algorithm, byte[] data, byte[] container, MetadataOptions? options = null)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            ArgumentNullException.ThrowIfNull(data);

            var carrier = MetadataCarrier.Load(container, options);
            algorithm.EmbedBytes(data, carrier);
            return carrier.ToArray();
        }

        public static byte[] ExtractBytes(this IStegAlgorithm<MetadataCarrier> algorithm, string path, MetadataOptions? options = null)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (path == null) throw new ArgumentNullException(nameof(path));

            return algorithm.ExtractBytes(MetadataCarrier.Load(path, options));
        }

        public static byte[] ExtractBytes(this IStegAlgorithm<MetadataCarrier> algorithm, Stream input, MetadataOptions? options = null)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (input == null) throw new ArgumentNullException(nameof(input));

            return algorithm.ExtractBytes(MetadataCarrier.Load(input, options));
        }

        /// <summary>Extract from container bytes held in memory.</summary>
        public static byte[] ExtractFromBytes(this IStegAlgorithm<MetadataCarrier> algorithm, byte[] container, MetadataOptions? options = null)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));

            return algorithm.ExtractBytes(MetadataCarrier.Load(container, options));
        }

        /// <exception cref="CapacityExceededException">The container cannot hold the sealed payload.</exception>
        public static void Embed(this StegoPipeline<MetadataCarrier> pipeline, byte[] data, string inputPath, string outputPath, StegoKey key, MetadataOptions? options = null)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(key);
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            var carrier = MetadataCarrier.Load(inputPath, options);
            pipeline.Embed(data, carrier, key);
            carrier.Save(outputPath);
        }

        /// <exception cref="CapacityExceededException">The container cannot hold the sealed payload.</exception>
        public static void Embed(this StegoPipeline<MetadataCarrier> pipeline, byte[] data, Stream input, Stream output, StegoKey key, MetadataOptions? options = null)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(key);
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (output == null) throw new ArgumentNullException(nameof(output));

            var carrier = MetadataCarrier.Load(input, options);
            pipeline.Embed(data, carrier, key);
            carrier.Save(output);
        }

        /// <summary>Seal and embed into the container bytes and return the new file.</summary>
        public static byte[] Embed(this StegoPipeline<MetadataCarrier> pipeline, byte[] data, byte[] container, StegoKey key, MetadataOptions? options = null)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(key);

            var carrier = MetadataCarrier.Load(container, options);
            pipeline.Embed(data, carrier, key);
            return carrier.ToArray();
        }

        public static ExtractResult Extract(this StegoPipeline<MetadataCarrier> pipeline, string path, StegoKey key, MetadataOptions? options = null)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            ArgumentNullException.ThrowIfNull(key);
            if (path == null) throw new ArgumentNullException(nameof(path));

            return pipeline.Extract(MetadataCarrier.Load(path, options), key);
        }

        public static ExtractResult Extract(this StegoPipeline<MetadataCarrier> pipeline, Stream input, StegoKey key, MetadataOptions? options = null)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            ArgumentNullException.ThrowIfNull(key);
            if (input == null) throw new ArgumentNullException(nameof(input));

            return pipeline.Extract(MetadataCarrier.Load(input, options), key);
        }

        /// <summary>Extract from container bytes held in memory.</summary>
        public static ExtractResult ExtractFromBytes(this StegoPipeline<MetadataCarrier> pipeline, byte[] container, StegoKey key, MetadataOptions? options = null)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            ArgumentNullException.ThrowIfNull(key);

            return pipeline.Extract(MetadataCarrier.Load(container, options), key);
        }
    }
}
