using System;
using System.Collections.Generic;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SteganoLib.Selection
{
    /// <summary>
    /// Skips smooth areas. Each candidate from the inner selector is scored by the
    /// variance of quantised grey levels in its 3x3 neighbourhood; only pixels at or
    /// above <see cref="MinVariance"/> are used. Grey levels use the top six bits,
    /// so the score does not change when the algorithm embeds. Content-aware inner
    /// selectors receive the image; their stable-bit requirements are preserved too.
    /// </summary>
    public sealed class AdaptivePixelSelector : IContentAwarePixelSelector
    {
        private readonly IPixelSelector _inner;
        private double _minVariance = 1.0;

        public AdaptivePixelSelector(IPixelSelector inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>
        /// Threshold on the neighbourhood variance of 6-bit grey levels (range 0 to 63).
        /// Flat regions score 0, a one-step gradient about 0.3, visible texture several units.
        /// Default 1.0.
        /// </summary>
        public double MinVariance
        {
            get => _minVariance;
            set
            {
                if (value < 0 || double.IsNaN(value))
                    throw new ArgumentOutOfRangeException(nameof(value));
                _minVariance = value;
            }
        }

        public int StableHighBits
        {
            get
            {
                int innerBits = _inner is IContentAwarePixelSelector aware ? aware.StableHighBits : 1;
                if (innerBits < 1 || innerBits > 7)
                    throw new InvalidOperationException("StableHighBits must be between 1 and 7.");
                return Math.Max(6, innerBits);
            }
        }

        public IPixelSelector Inner => _inner;

        /// <summary>Not supported: this selector needs the pixel data. Use <see cref="Pixels(Image{Rgba32})"/>.</summary>
        public IEnumerable<Point> Pixels(int width, int height)
        {
            throw new NotSupportedException("AdaptivePixelSelector needs the image; call Pixels(Image<Rgba32>).");
        }

        public IEnumerable<Point> Pixels(Image<Rgba32> image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            return Filter(image);
        }

        /// <summary>Texture score of the pixel at <paramref name="x"/>, <paramref name="y"/>.</summary>
        public double Score(Image<Rgba32> image, int x, int y)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            return Variance(image, x, y);
        }

        private IEnumerable<Point> Filter(Image<Rgba32> image)
        {
            var candidates = _inner is IContentAwarePixelSelector aware
                ? aware.Pixels(image)
                : _inner.Pixels(image.Width, image.Height);
            foreach (var p in candidates)
            {
                if (Variance(image, p.X, p.Y) >= _minVariance)
                    yield return p;
            }
        }

        private double Variance(Image<Rgba32> image, int x, int y)
        {
            const int shift = 2; // The score always uses six bits, even if the inner selector needs seven.
            int x0 = Math.Max(0, x - 1), x1 = Math.Min(image.Width - 1, x + 1);
            int y0 = Math.Max(0, y - 1), y1 = Math.Min(image.Height - 1, y + 1);

            double sum = 0, sumSq = 0;
            int n = 0;
            for (int yy = y0; yy <= y1; yy++)
            {
                for (int xx = x0; xx <= x1; xx++)
                {
                    var p = image[xx, yy];
                    double grey = ((p.R >> shift) + (p.G >> shift) + (p.B >> shift)) / 3.0;
                    sum += grey;
                    sumSq += grey * grey;
                    n++;
                }
            }

            double mean = sum / n;
            return sumSq / n - mean * mean;
        }
    }
}
