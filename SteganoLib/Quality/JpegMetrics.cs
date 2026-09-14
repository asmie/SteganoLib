using System;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Jpeg;

namespace SteganoLib.Quality
{
    /// <summary>How far a stego JPEG is from its cover, in the coefficient and the pixel domain.</summary>
    public sealed class JpegComparison
    {
        internal JpegComparison(long changedCoefficients, long totalCoefficients, long nonZeroCoefficients, int maxAbsoluteChange, ImageComparison pixels)
        {
            ChangedCoefficients = changedCoefficients;
            TotalCoefficients = totalCoefficients;
            NonZeroCoefficients = nonZeroCoefficients;
            MaxAbsoluteChange = maxAbsoluteChange;
            Pixels = pixels;
        }

        /// <summary>Quantised DCT coefficients that differ.</summary>
        public long ChangedCoefficients { get; }

        public long TotalCoefficients { get; }

        /// <summary>Non-zero AC coefficients of the cover, the population most JPEG algorithms embed into.</summary>
        public long NonZeroCoefficients { get; }

        /// <summary>Changes relative to the cover's non-zero AC coefficients.</summary>
        public double ChangeRate => NonZeroCoefficients == 0 ? 0 : (double)ChangedCoefficients / NonZeroCoefficients;

        public int MaxAbsoluteChange { get; }

        /// <summary>Distortion of the decoded pictures.</summary>
        public ImageComparison Pixels { get; }
    }

    /// <summary>Distortion measures between two JPEGs with the same layout.</summary>
    public static class JpegMetrics
    {
        public static JpegComparison Compare(JpegImage cover, JpegImage stego)
        {
            if (cover == null) throw new ArgumentNullException(nameof(cover));
            if (stego == null) throw new ArgumentNullException(nameof(stego));
            if (cover.Components.Count != stego.Components.Count)
                throw new ArgumentException("JPEGs differ in component count.", nameof(stego));

            long changed = 0, total = 0, nonZero = 0;
            int maxChange = 0;
            for (int c = 0; c < cover.Components.Count; c++)
            {
                var a = cover.Components[c].Coefficients;
                var b = stego.Components[c].Coefficients;
                if (a.Length != b.Length)
                    throw new ArgumentException("JPEGs differ in size.", nameof(stego));

                total += a.Length;
                for (int i = 0; i < a.Length; i++)
                {
                    if (i % 64 != 0 && a[i] != 0)
                        nonZero++;
                    int diff = Math.Abs(a[i] - b[i]);
                    if (diff != 0)
                    {
                        changed++;
                        maxChange = Math.Max(maxChange, diff);
                    }
                }
            }

            using var coverPixels = Image.Load<Rgba32>(cover.ToArray());
            using var stegoPixels = Image.Load<Rgba32>(stego.ToArray());
            return new JpegComparison(changed, total, nonZero, maxChange, ImageMetrics.Compare(coverPixels, stegoPixels));
        }
    }
}
