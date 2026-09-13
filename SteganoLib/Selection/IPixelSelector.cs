using System.Collections.Generic;

using SixLabors.ImageSharp;

namespace SteganoLib.Selection
{
    /// <summary>
    /// Decides which pixels carry payload bits and in what order.
    /// Each call to <see cref="Pixels"/> must produce the same sequence for the
    /// same configuration, and must not return a pixel twice.
    /// </summary>
    public interface IPixelSelector
    {
        IEnumerable<Point> Pixels(int width, int height);
    }
}
