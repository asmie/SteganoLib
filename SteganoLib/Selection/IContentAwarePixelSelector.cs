using System.Collections.Generic;
using System.Linq;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SteganoLib.Selection
{
    /// <summary>
    /// A selector that looks at pixel values to decide which pixels to use.
    /// Because embedding changes those values, the selector only reads the upper
    /// <see cref="StableHighBits"/> bits of each channel, and the algorithm promises
    /// not to change them.
    /// </summary>
    public interface IContentAwarePixelSelector : IPixelSelector
    {
        /// <summary>Pixels of <paramref name="image"/> that may carry data, in embedding order.</summary>
        IEnumerable<Point> Pixels(Image<Rgba32> image);

        /// <summary>Exact selected pixel count for this image; the default enumerates the selection.</summary>
        long Count(Image<Rgba32> image) => Pixels(image).LongCount();

        /// <summary>Number of high bits per channel the algorithm must leave untouched (1 to 7).</summary>
        int StableHighBits { get; }
    }
}
