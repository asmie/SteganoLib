using SteganoLib.Audio;

namespace SteganoLib.Algorithms
{
    /// <summary>Price of changing one sample by one step, used by the syndrome-trellis coder.</summary>
    public interface ISampleCostModel
    {
        /// <summary>Cost of changing the sample at <c>index</c> into <see cref="PcmAudio.Samples"/>.</summary>
        /// <returns>Non-negative cost; <see cref="double.PositiveInfinity"/> forbids the change.</returns>
        double Cost(PcmAudio audio, long index);
    }
}
