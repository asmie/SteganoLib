using System;
using System.Collections.Generic;
using System.Numerics;

using SteganoLib.Audio;
using SteganoLib.Crypto;

namespace SteganoLib.Algorithms
{
    /// <summary>
    /// Phase coding (Bender et al., 1996) for PCM audio. The first channel is cut into
    /// segments of <see cref="SegmentLength"/> samples. Each payload bit sets the phase
    /// of one frequency bin in the first segment to plus or minus pi/2; later segments
    /// keep their original phase differences so the signal stays continuous. The ear is
    /// nearly deaf to absolute phase, and the mark survives amplitude changes, but not
    /// resampling or lossy compression.
    /// <para>
    /// Bin order is keyed. A 4-byte big-endian length header precedes the payload.
    /// Bins whose magnitude is too small to survive rounding are raised to a floor.
    /// </para>
    /// </summary>
    public sealed class PhaseCoding : IStegAlgorithm<PcmAudio>
    {
        private const string Purpose = "SteganoLib/phase-bin-permutation/v1";
        private const int HeaderSize = 4;
        private const int MinSegment = 64;

        private readonly byte[] _keyMaterial;
        private int _segmentLength;
        private double _magnitudeFloorFactor = 24;

        public PhaseCoding(StegoKey key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            _keyMaterial = key.Derive(Purpose, FeistelPermutation.KeySize);
        }

        /// <summary>
        /// Samples per segment, a power of two of at least 64. 0 (default) uses the
        /// largest power of two that fits the audio, giving the most capacity.
        /// A recording shorter than the configured segment has zero capacity. When
        /// no complete segment can hold the header, embedding empty data is a no-op.
        /// </summary>
        public int SegmentLength
        {
            get => _segmentLength;
            set
            {
                if (value != 0 && (value < MinSegment || !Fft.IsPowerOfTwo(value)))
                    throw new ArgumentOutOfRangeException(nameof(value), "Must be 0 or a power of two of at least 64.");
                _segmentLength = value;
            }
        }

        /// <summary>
        /// Minimum bin magnitude, as a multiple of the expected rounding noise, for a
        /// bin to hold a bit reliably. Weaker bins are amplified to this level. Default 24.
        /// </summary>
        public double MagnitudeFloorFactor
        {
            get => _magnitudeFloorFactor;
            set
            {
                if (value < 1 || !double.IsFinite(value))
                    throw new ArgumentOutOfRangeException(nameof(value));
                _magnitudeFloorFactor = value;
            }
        }

        /// <inheritdoc />
        public void EmbedBytes(byte[] data, PcmAudio audio)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (audio == null)
                throw new ArgumentNullException(nameof(audio));

            long capacity = Capacity(audio);
            if (data.Length > capacity)
                throw new CapacityExceededException(data.Length, capacity);

            int length = EffectiveSegmentLength(audio);
            // Empty raw payloads are a no-op when even the length header cannot fit.
            if (length == 0 || length / 2 - 1 < HeaderSize * 8)
                return;

            int segments = audio.FrameCount / length;
            var spectra = new Complex[segments][];
            for (int s = 0; s < segments; s++)
            {
                spectra[s] = Segment(audio, s, length);
                Fft.Forward(spectra[s]);
            }

            var framed = new byte[HeaderSize + data.Length];
            framed[0] = (byte)(data.Length >> 24);
            framed[1] = (byte)(data.Length >> 16);
            framed[2] = (byte)(data.Length >> 8);
            framed[3] = (byte)data.Length;
            Array.Copy(data, 0, framed, HeaderSize, data.Length);

            var original = (Complex[])spectra[0].Clone();
            double floor = MagnitudeFloor(length);
            int bitIndex = 0;
            foreach (int bin in Bins(length))
            {
                if (bitIndex >= framed.Length * 8)
                    break;

                bool bit = ((framed[bitIndex / 8] >> (bitIndex % 8)) & 1) == 1;
                double magnitude = Math.Max(spectra[0][bin].Magnitude, floor);
                spectra[0][bin] = Complex.FromPolarCoordinates(magnitude, bit ? Math.PI / 2 : -Math.PI / 2);
                spectra[0][length - bin] = Complex.Conjugate(spectra[0][bin]);
                bitIndex++;
            }

            // Later segments follow the first with their original phase differences.
            for (int s = 1; s < segments; s++)
            {
                var previousOriginal = s == 1 ? original : PreviousOriginal(audio, s - 1, length);
                for (int bin = 1; bin < length / 2; bin++)
                {
                    double delta = spectra[s][bin].Phase - previousOriginal[bin].Phase;
                    double phase = spectra[s - 1][bin].Phase + delta;
                    spectra[s][bin] = Complex.FromPolarCoordinates(spectra[s][bin].Magnitude, phase);
                    spectra[s][length - bin] = Complex.Conjugate(spectra[s][bin]);
                }
            }

            for (int s = 0; s < segments; s++)
            {
                Fft.Inverse(spectra[s]);
                WriteSegment(audio, s, length, spectra[s]);
            }
        }

        /// <inheritdoc />
        public byte[] ExtractBytes(PcmAudio audio)
        {
            if (audio == null)
                throw new ArgumentNullException(nameof(audio));

            int length = EffectiveSegmentLength(audio);
            if (length == 0 || length / 2 - 1 < HeaderSize * 8)
                return Array.Empty<byte>();

            long capacity = Capacity(audio);
            var spectrum = Segment(audio, 0, length);
            Fft.Forward(spectrum);

            using var bins = Bins(length).GetEnumerator();
            var header = new byte[HeaderSize];
            if (!ReadBits(spectrum, bins, header))
                return Array.Empty<byte>();

            int dataLength = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (dataLength < 0 || dataLength > capacity)
                return Array.Empty<byte>();

            var result = new byte[dataLength];
            if (!ReadBits(spectrum, bins, result))
                return Array.Empty<byte>();
            return result;
        }

        /// <inheritdoc />
        public long Capacity(PcmAudio audio)
        {
            if (audio == null)
                throw new ArgumentNullException(nameof(audio));
            int length = EffectiveSegmentLength(audio);
            if (length == 0)
                return 0;
            long bins = length / 2 - 1; // bins 1 .. N/2 - 1; DC and Nyquist have no usable phase
            return Math.Max(0, bins / 8 - HeaderSize);
        }

        private int EffectiveSegmentLength(PcmAudio audio)
        {
            if (audio.FrameCount < MinSegment || _segmentLength > audio.FrameCount)
                return 0;
            if (_segmentLength != 0)
                return _segmentLength;

            int length = MinSegment;
            while ((long)length * 2 <= audio.FrameCount)
                length *= 2;
            return length;
        }

        // Rounding to integers adds white noise of variance 1/12 per sample; a bin sees
        // its sum over the segment, so the noise magnitude is about sqrt(N / 12).
        private double MagnitudeFloor(int length) => _magnitudeFloorFactor * Math.Sqrt(length / 12.0);

        private IEnumerable<int> Bins(int length)
        {
            int count = length / 2 - 1;
            var permutation = new FeistelPermutation(_keyMaterial, count);
            for (int i = 0; i < count; i++)
                yield return (int)permutation.Permute(i) + 1;
        }

        private static bool ReadBits(Complex[] spectrum, IEnumerator<int> bins, byte[] target)
        {
            for (int i = 0; i < target.Length * 8; i++)
            {
                if (!bins.MoveNext())
                    return false;
                if (spectrum[bins.Current].Phase > 0)
                    target[i / 8] |= (byte)(1 << (i % 8));
            }
            return true;
        }

        private static Complex[] Segment(PcmAudio audio, int segment, int length)
        {
            var data = new Complex[length];
            int channels = audio.Channels;
            long start = (long)segment * length * channels;
            for (int i = 0; i < length; i++)
                data[i] = audio.Samples[start + i * channels];
            return data;
        }

        private static Complex[] PreviousOriginal(PcmAudio audio, int segment, int length)
        {
            // Segments are rewritten only after all spectra are adjusted, so the audio still holds the original.
            var data = Segment(audio, segment, length);
            Fft.Forward(data);
            return data;
        }

        private static void WriteSegment(PcmAudio audio, int segment, int length, Complex[] data)
        {
            int channels = audio.Channels;
            long start = (long)segment * length * channels;
            for (int i = 0; i < length; i++)
            {
                long value = (long)Math.Round(data[i].Real);
                audio.Samples[start + i * channels] = (int)Math.Clamp(value, audio.MinValue, audio.MaxValue);
            }
        }
    }
}
