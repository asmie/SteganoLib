using System;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Audio;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;

namespace SteganoLib.Quality
{
    /// <summary>
    /// Embed and measure in one call. The carrier is modified in place as usual; a copy
    /// of the cover is kept just long enough to compare against.
    /// </summary>
    public static class QualityExtensions
    {
        /// <exception cref="CapacityExceededException">The image cannot hold <paramref name="data"/>.</exception>
        public static EmbeddingReport<ImageComparison> EmbedWithReport(this IStegAlgorithm<Image<Rgba32>> algorithm, byte[] data, Image<Rgba32> image)
        {
            Check(algorithm, data, image);
            using var cover = image.Clone();
            algorithm.EmbedBytes(data, image);
            return new EmbeddingReport<ImageComparison>(data.Length, algorithm.Capacity(cover), ImageMetrics.Compare(cover, image));
        }

        /// <exception cref="CapacityExceededException">The image cannot hold the sealed payload.</exception>
        public static EmbeddingReport<ImageComparison> EmbedWithReport(this StegoPipeline<Image<Rgba32>> pipeline, byte[] data, Image<Rgba32> image, StegoKey key)
        {
            Check(pipeline, data, image);
            using var cover = image.Clone();
            pipeline.Embed(data, image, key);
            return new EmbeddingReport<ImageComparison>(data.Length, pipeline.Capacity(cover), ImageMetrics.Compare(cover, image));
        }

        /// <exception cref="CapacityExceededException">The recording cannot hold <paramref name="data"/>.</exception>
        public static EmbeddingReport<AudioComparison> EmbedWithReport(this IStegAlgorithm<PcmAudio> algorithm, byte[] data, PcmAudio audio)
        {
            Check(algorithm, data, audio);
            var cover = audio.Clone();
            algorithm.EmbedBytes(data, audio);
            return new EmbeddingReport<AudioComparison>(data.Length, algorithm.Capacity(cover), AudioMetrics.Compare(cover, audio));
        }

        /// <exception cref="CapacityExceededException">The recording cannot hold the sealed payload.</exception>
        public static EmbeddingReport<AudioComparison> EmbedWithReport(this StegoPipeline<PcmAudio> pipeline, byte[] data, PcmAudio audio, StegoKey key)
        {
            Check(pipeline, data, audio);
            var cover = audio.Clone();
            pipeline.Embed(data, audio, key);
            return new EmbeddingReport<AudioComparison>(data.Length, pipeline.Capacity(cover), AudioMetrics.Compare(cover, audio));
        }

        /// <exception cref="CapacityExceededException">The JPEG cannot hold <paramref name="data"/>.</exception>
        public static EmbeddingReport<JpegComparison> EmbedWithReport(this IStegAlgorithm<JpegImage> algorithm, byte[] data, JpegImage image)
        {
            Check(algorithm, data, image);
            var cover = image.Clone();
            algorithm.EmbedBytes(data, image);
            return new EmbeddingReport<JpegComparison>(data.Length, algorithm.Capacity(cover), JpegMetrics.Compare(cover, image));
        }

        /// <exception cref="CapacityExceededException">The JPEG cannot hold the sealed payload.</exception>
        public static EmbeddingReport<JpegComparison> EmbedWithReport(this StegoPipeline<JpegImage> pipeline, byte[] data, JpegImage image, StegoKey key)
        {
            Check(pipeline, data, image);
            var cover = image.Clone();
            pipeline.Embed(data, image, key);
            return new EmbeddingReport<JpegComparison>(data.Length, pipeline.Capacity(cover), JpegMetrics.Compare(cover, image));
        }

        private static void Check(object embedder, byte[] data, object carrier)
        {
            if (embedder == null) throw new ArgumentNullException(nameof(embedder));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (carrier == null) throw new ArgumentNullException(nameof(carrier));
        }
    }
}
