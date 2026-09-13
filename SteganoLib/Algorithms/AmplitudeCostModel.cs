using System;

using SteganoLib.Audio;

namespace SteganoLib.Algorithms
{
    /// <summary>
    /// Changes are cheap where the signal is busy and expensive where it is quiet,
    /// following the ear's masking behaviour. Activity is the mean absolute step to
    /// the neighbouring frames of the same channel; cost = 1 / (activity + <see cref="Smoothing"/>).
    /// </summary>
    public sealed class AmplitudeCostModel : ISampleCostModel
    {
        private double _smoothing = 1.0;

        /// <summary>Added to the activity so silence gets a large but finite cost. Default 1.0.</summary>
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

        public double Cost(PcmAudio audio, long index)
        {
            if (audio == null)
                throw new ArgumentNullException(nameof(audio));

            var samples = audio.Samples;
            int channels = audio.Channels;
            long previous = index - channels;
            long next = index + channels;

            double activity = 0;
            int terms = 0;
            if (previous >= 0)
            {
                activity += Math.Abs((double)samples[index] - samples[previous]);
                terms++;
            }
            if (next < samples.Length)
            {
                activity += Math.Abs((double)samples[next] - samples[index]);
                terms++;
            }
            if (terms > 0)
                activity /= terms;

            return 1.0 / (activity + _smoothing);
        }
    }
}
