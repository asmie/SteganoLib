using System;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SteganoLib.Algorithms
{
    /// <summary>
    /// Changes in smooth areas cost more than changes in textured ones. The cost is
    /// 1 / (variance + <see cref="Smoothing"/>) over the 3x3 neighbourhood of the
    /// selected channel.
    /// </summary>
    public sealed class TextureCostModel : IPixelCostModel
    {
        private double _smoothing = 0.5;

        /// <summary>Added to the variance so flat regions get a large but finite cost. Default 0.5.</summary>
        public double Smoothing
        {
            get => _smoothing;
            set
            {
                if (value <= 0 || double.IsNaN(value))
                    throw new ArgumentOutOfRangeException(nameof(value));
                _smoothing = value;
            }
        }

        public double Cost(Image<Rgba32> image, int x, int y, int channel)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            int x0 = Math.Max(0, x - 1), x1 = Math.Min(image.Width - 1, x + 1);
            int y0 = Math.Max(0, y - 1), y1 = Math.Min(image.Height - 1, y + 1);

            double sum = 0, sumSq = 0;
            int n = 0;
            for (int yy = y0; yy <= y1; yy++)
            {
                for (int xx = x0; xx <= x1; xx++)
                {
                    var p = image[xx, yy];
                    double v = channel switch { 0 => p.R, 1 => p.G, _ => p.B };
                    sum += v;
                    sumSq += v * v;
                    n++;
                }
            }

            double mean = sum / n;
            double variance = Math.Max(0, sumSq / n - mean * mean);
            return 1.0 / (variance + _smoothing);
        }
    }
}
