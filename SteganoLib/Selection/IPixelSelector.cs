using System.Collections.Generic;
using System.Linq;

using SixLabors.ImageSharp;

namespace SteganoLib.Selection
{
    /// <summary>
    /// Decides which pixels carry payload bits and in what order.
    /// Each call to <see cref="Pixels"/> must produce the same sequence for the
    /// same configuration and dimensions. It may return a subset, but must terminate,
    /// stay within the image bounds and never return a pixel twice.
    /// </summary>
    public interface IPixelSelector
    {
        IEnumerable<Point> Pixels(int width, int height);

        /// <summary>
        /// Number of pixels returned by <see cref="Pixels"/>. The default enumerates
        /// the sequence; override when the exact count is available without traversal.
        /// </summary>
        long Count(int width, int height) => Pixels(width, height).LongCount();
    }
}
