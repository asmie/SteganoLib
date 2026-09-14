using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SteganoLib.Algorithms
{
    /// <summary>
    /// Price of changing one channel of one pixel by one step. Used by the
    /// syndrome-trellis coder to choose which pixels to change; the receiver never
    /// needs it, so it may look at anything in the cover image. Applies to payload
    /// slots only; the plain algorithm header does not consult the cost model.
    /// </summary>
    public interface IPixelCostModel
    {
        /// <summary>Cost of changing channel 0 (red), 1 (green) or 2 (blue) of the pixel at x, y.</summary>
        /// <returns>Non-negative cost; <see cref="double.PositiveInfinity"/> forbids the change.</returns>
        double Cost(Image<Rgba32> image, int x, int y, int channel);
    }
}
