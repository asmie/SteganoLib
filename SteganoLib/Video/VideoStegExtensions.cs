#nullable enable

using System;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;
using SteganoLib.Payload;

namespace SteganoLib.Video
{
    /// <summary>AVI file helpers: RGB videos take an image frame algorithm, MJPEG videos a JPEG one.</summary>
    public static class VideoStegExtensions
    {
        /// <exception cref="CapacityExceededException">The video cannot hold <paramref name="data"/>.</exception>
        public static void EmbedBytes(this IStegAlgorithm<IFrameSequence<Image<Rgba32>>> algorithm, byte[] data, string inputPath, string outputPath)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            ArgumentNullException.ThrowIfNull(data);
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            var video = AviVideo.Load(inputPath);
            algorithm.EmbedBytes(data, video.RgbFrames);
            video.Save(outputPath);
        }

        /// <exception cref="CapacityExceededException">The video cannot hold <paramref name="data"/>.</exception>
        public static void EmbedBytes(this IStegAlgorithm<IFrameSequence<JpegImage>> algorithm, byte[] data, string inputPath, string outputPath)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            ArgumentNullException.ThrowIfNull(data);
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            var video = AviVideo.Load(inputPath);
            algorithm.EmbedBytes(data, video.JpegFrames);
            video.Save(outputPath);
        }

        public static byte[] ExtractBytes(this IStegAlgorithm<IFrameSequence<Image<Rgba32>>> algorithm, string path)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (path == null) throw new ArgumentNullException(nameof(path));

            return algorithm.ExtractBytes(AviVideo.Load(path).RgbFrames);
        }

        public static byte[] ExtractBytes(this IStegAlgorithm<IFrameSequence<JpegImage>> algorithm, string path)
        {
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (path == null) throw new ArgumentNullException(nameof(path));

            return algorithm.ExtractBytes(AviVideo.Load(path).JpegFrames);
        }

        /// <exception cref="CapacityExceededException">The video cannot hold the sealed payload.</exception>
        public static void Embed(this StegoPipeline<IFrameSequence<Image<Rgba32>>> pipeline, byte[] data, string inputPath, string outputPath, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(key);
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            var video = AviVideo.Load(inputPath);
            pipeline.Embed(data, video.RgbFrames, key);
            video.Save(outputPath);
        }

        /// <exception cref="CapacityExceededException">The video cannot hold the sealed payload.</exception>
        public static void Embed(this StegoPipeline<IFrameSequence<JpegImage>> pipeline, byte[] data, string inputPath, string outputPath, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(key);
            if (inputPath == null) throw new ArgumentNullException(nameof(inputPath));
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));

            var video = AviVideo.Load(inputPath);
            pipeline.Embed(data, video.JpegFrames, key);
            video.Save(outputPath);
        }

        public static ExtractResult Extract(this StegoPipeline<IFrameSequence<Image<Rgba32>>> pipeline, string path, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            ArgumentNullException.ThrowIfNull(key);
            if (path == null) throw new ArgumentNullException(nameof(path));

            return pipeline.Extract(AviVideo.Load(path).RgbFrames, key);
        }

        public static ExtractResult Extract(this StegoPipeline<IFrameSequence<JpegImage>> pipeline, string path, StegoKey key)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            ArgumentNullException.ThrowIfNull(key);
            if (path == null) throw new ArgumentNullException(nameof(path));

            return pipeline.Extract(AviVideo.Load(path).JpegFrames, key);
        }
    }
}
