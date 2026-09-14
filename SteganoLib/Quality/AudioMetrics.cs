using System;

using SteganoLib.Audio;

namespace SteganoLib.Quality
{
    /// <summary>How far a stego recording is from its cover.</summary>
    public sealed class AudioComparison
    {
        internal AudioComparison(double snr, double psnr, long changedSamples, long totalSamples, int maxAbsoluteError)
        {
            Snr = snr;
            Psnr = psnr;
            ChangedSamples = changedSamples;
            TotalSamples = totalSamples;
            MaxAbsoluteError = maxAbsoluteError;
        }

        /// <summary>Signal power over the power of the difference, in dB; infinite for identical recordings.</summary>
        public double Snr { get; }

        /// <summary>Full-scale peak over the root mean square difference, in dB.</summary>
        public double Psnr { get; }

        public long ChangedSamples { get; }

        public long TotalSamples { get; }

        public double ChangeRate => TotalSamples == 0 ? 0 : (double)ChangedSamples / TotalSamples;

        public int MaxAbsoluteError { get; }
    }

    /// <summary>Distortion measures between two PCM recordings with the same layout.</summary>
    public static class AudioMetrics
    {
        public static AudioComparison Compare(PcmAudio cover, PcmAudio stego)
        {
            if (cover == null) throw new ArgumentNullException(nameof(cover));
            if (stego == null) throw new ArgumentNullException(nameof(stego));
            if (cover.Samples.Length != stego.Samples.Length || cover.Channels != stego.Channels || cover.BitsPerSample != stego.BitsPerSample)
                throw new ArgumentException("Recordings differ in length, channel count or bit depth.", nameof(stego));

            double signal = 0, noise = 0;
            long changed = 0;
            int maxError = 0;
            for (int i = 0; i < cover.Samples.Length; i++)
            {
                double s = cover.Samples[i];
                double d = (double)stego.Samples[i] - s;
                signal += s * s;
                noise += d * d;
                if (d != 0)
                {
                    changed++;
                    maxError = Math.Max(maxError, (int)Math.Abs(d));
                }
            }

            long total = cover.Samples.Length;
            double snr = noise == 0 ? double.PositiveInfinity : 10 * Math.Log10(signal / noise);
            double peak = (double)cover.MaxValue;
            double psnr = noise == 0 ? double.PositiveInfinity : 10 * Math.Log10(peak * peak * total / noise);
            return new AudioComparison(snr, psnr, changed, total, maxError);
        }

        public static double Snr(PcmAudio cover, PcmAudio stego) => Compare(cover, stego).Snr;
    }
}
