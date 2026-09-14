using SteganoLib.Audio;

namespace SteganoLib.Algorithms
{
    /// <summary>
    /// Cost per changed payload bit, used by the syndrome-trellis coder. With multiple
    /// bits per sample, each changed bit incurs this cost. The plain header ignores costs.
    /// </summary>
    public interface ISampleCostModel
    {
        /// <summary>Cost of changing the sample at <c>index</c> into <see cref="PcmAudio.Samples"/>.</summary>
        /// <returns>Non-negative cost; <see cref="double.PositiveInfinity"/> forbids the change.</returns>
        double Cost(PcmAudio audio, long index);
    }
}
