using System;
using System.IO;

using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Payload;

namespace SteganoLib.Audio
{
    /// <summary>File and stream helpers for WAV carriers.</summary>
    public static class AudioStegExtensions
    {
        /// <exception cref="CapacityExceededException">The audio cannot hold <paramref name="data"/>.</exception>
        public static void EmbedBytes(this IStegAlgorithm<PcmAudio> algorithm, byte[] data, string inputPath, string outputPath)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            var audio = PcmAudio.Load(inputPath);
            algorithm.EmbedBytes(data, audio);
            audio.Save(outputPath);
        }

        /// <exception cref="CapacityExceededException">The audio cannot hold <paramref name="data"/>.</exception>
        public static void EmbedBytes(this IStegAlgorithm<PcmAudio> algorithm, byte[] data, Stream input, Stream output)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (output == null) throw new ArgumentNullException(nameof(output));

            var audio = PcmAudio.Load(input);
            algorithm.EmbedBytes(data, audio);
            audio.Save(output);
        }

        public static byte[] ExtractBytes(this IStegAlgorithm<PcmAudio> algorithm, string path)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (path == null) throw new ArgumentNullException(nameof(path));

            return algorithm.ExtractBytes(PcmAudio.Load(path));
        }

        public static byte[] ExtractBytes(this IStegAlgorithm<PcmAudio> algorithm, Stream input)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (input == null) throw new ArgumentNullException(nameof(input));

            return algorithm.ExtractBytes(PcmAudio.Load(input));
        }

        /// <exception cref="CapacityExceededException">The audio cannot hold the sealed payload.</exception>
        public static void Embed(this StegoPipeline<PcmAudio> pipeline, byte[] data, string inputPath, string outputPath, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            var audio = PcmAudio.Load(inputPath);
            pipeline.Embed(data, audio, key);
            audio.Save(outputPath);
        }

        /// <exception cref="CapacityExceededException">The audio cannot hold the sealed payload.</exception>
        public static void Embed(this StegoPipeline<PcmAudio> pipeline, byte[] data, Stream input, Stream output, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (output == null) throw new ArgumentNullException(nameof(output));

            var audio = PcmAudio.Load(input);
            pipeline.Embed(data, audio, key);
            audio.Save(output);
        }

        public static ExtractResult Extract(this StegoPipeline<PcmAudio> pipeline, string path, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (path == null) throw new ArgumentNullException(nameof(path));

            return pipeline.Extract(PcmAudio.Load(path), key);
        }

        public static ExtractResult Extract(this StegoPipeline<PcmAudio> pipeline, Stream input, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (input == null) throw new ArgumentNullException(nameof(input));

            return pipeline.Extract(PcmAudio.Load(input), key);
        }
    }
}
