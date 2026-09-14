using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Audio;
using SteganoLib.Coding;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;
using SteganoLib.Metadata;
using SteganoLib.Payload;
using SteganoLib.Quality;
using SteganoLib.Selection;
using SteganoLib.Steganalysis;
using SteganoLib.Text;

namespace SteganoLib.Cli
{
    /// <summary>Settings shared by embed, extract and capacity.</summary>
    internal sealed class CarrierOptions
    {
        public string Input { get; set; }

        public CarrierKind Kind { get; set; } = CarrierKind.Auto;

        public TextMethod TextMethod { get; set; } = TextMethod.ZeroWidth;

        /// <summary>Reed-Solomon parity bytes per block, 0 for none.</summary>
        public int ErrorCorrection { get; set; }

        public bool Compress { get; set; }

        public string Passphrase { get; set; }

        public string KeyFile { get; set; }
    }

    /// <summary>The work behind each command, independent of the command-line parser.</summary>
    internal sealed class Operations
    {
        private readonly TextWriter _out;

        public Operations(TextWriter output)
        {
            _out = output ?? throw new ArgumentNullException(nameof(output));
        }

        public int KeyGen(string outputPath)
        {
            var key = RandomNumberGenerator.GetBytes(32);
            if (outputPath == null)
            {
                _out.WriteLine(Convert.ToBase64String(key));
            }
            else
            {
                File.WriteAllBytes(outputPath, key);
                _out.WriteLine($"Wrote a 32-byte key to {outputPath}. Keep it secret; it is needed to extract.");
            }
            return ExitCodes.Success;
        }

        public int Embed(CarrierOptions options, string outputPath, byte[] data)
        {
            RequireFile(options.Input);
            if (outputPath == null)
                throw new CliException("--out is required.", ExitCodes.Usage);

            var key = LoadKey(options);
            var kind = CarrierKinds.Detect(options.Kind, options.Input);
            try
            {
                switch (kind)
                {
                    case CarrierKind.Image:
                        EmbedImage(options, outputPath, data, key);
                        break;
                    case CarrierKind.Jpeg:
                        EmbedInto(Pipeline(new F5(key), options), data, key, outputPath, JpegImage.Load(options.Input), (c, p) => c.Save(p), c => c.Clone(),
                            (cover, stego) => DescribeJpeg(JpegMetrics.Compare(cover, stego)));
                        break;
                    case CarrierKind.Wav:
                        EmbedInto(Pipeline(new AudioLsb(new KeyedSampleSelector(key)), options), data, key, outputPath, PcmAudio.Load(options.Input), (c, p) => c.Save(p), c => c.Clone(),
                            (cover, stego) => DescribeAudio(AudioMetrics.Compare(cover, stego)));
                        break;
                    case CarrierKind.Text:
                        EmbedInto(Pipeline(TextAlgorithm(options.TextMethod, key), options), data, key, outputPath, TextCarrier.Load(options.Input), (c, p) => c.Save(p), null, null);
                        break;
                    case CarrierKind.Metadata:
                        EmbedInto(Pipeline(new MetadataCoding(), options), data, key, outputPath, MetadataCarrier.Load(options.Input), (c, p) => c.Save(p), null, null);
                        break;
                    default:
                        throw new CliException($"Carrier {kind} is not supported for embedding.", ExitCodes.Usage);
                }
            }
            catch (CapacityExceededException e)
            {
                throw new CliException($"Payload too large: {e.Message}");
            }
            return ExitCodes.Success;
        }

        public int Extract(CarrierOptions options, string outputPath, bool asText)
        {
            RequireFile(options.Input);
            var key = LoadKey(options);
            var kind = CarrierKinds.Detect(options.Kind, options.Input);

            ExtractResult result = kind switch
            {
                CarrierKind.Image => ExtractImage(options, key),
                CarrierKind.Jpeg => Pipeline(new F5(key), options).Extract(JpegImage.Load(options.Input), key),
                CarrierKind.Wav => Pipeline(new AudioLsb(new KeyedSampleSelector(key)), options).Extract(PcmAudio.Load(options.Input), key),
                CarrierKind.Text => Pipeline(TextAlgorithm(options.TextMethod, key), options).Extract(TextCarrier.Load(options.Input), key),
                CarrierKind.Metadata => Pipeline(new MetadataCoding(), options).Extract(MetadataCarrier.Load(options.Input), key),
                _ => throw new CliException($"Carrier {kind} is not supported for extraction.", ExitCodes.Usage),
            };

            switch (result.Status)
            {
                case ExtractionStatus.Success:
                    break;
                case ExtractionStatus.NotFound:
                    throw new CliException("No payload found. Wrong key, wrong carrier settings, or nothing embedded.");
                case ExtractionStatus.AuthenticationFailed:
                    throw new CliException("A payload is present but does not authenticate: wrong key or damaged file.");
                default:
                    throw new CliException("A payload is present but was written by an unsupported version.");
            }

            if (outputPath != null)
            {
                File.WriteAllBytes(outputPath, result.Data);
                _out.WriteLine($"Extracted {result.Data.Length} bytes to {outputPath}.");
            }
            else if (asText)
            {
                _out.WriteLine(Encoding.UTF8.GetString(result.Data));
            }
            else
            {
                _out.WriteLine($"Extracted {result.Data.Length} bytes. Pass --out to save them or --as-text to print them.");
            }
            return ExitCodes.Success;
        }

        public int Capacity(CarrierOptions options)
        {
            RequireFile(options.Input);
            var key = TryLoadKey(options) ?? StegoKey.FromBytes(new byte[] { 0 }); // capacity does not depend on the key
            var kind = CarrierKinds.Detect(options.Kind, options.Input);

            (long raw, long net) = kind switch
            {
                CarrierKind.Image => CapacityOf(ImageAlgorithm(key), options, Image.Load<Rgba32>(options.Input)),
                CarrierKind.Jpeg => CapacityOf(new F5(key), options, JpegImage.Load(options.Input)),
                CarrierKind.Wav => CapacityOf(new AudioLsb(new KeyedSampleSelector(key)), options, PcmAudio.Load(options.Input)),
                CarrierKind.Text => CapacityOf(TextAlgorithm(options.TextMethod, key), options, TextCarrier.Load(options.Input)),
                CarrierKind.Metadata => CapacityOf(new MetadataCoding(), options, MetadataCarrier.Load(options.Input)),
                _ => throw new CliException($"Carrier {kind} is not supported.", ExitCodes.Usage),
            };

            _out.WriteLine($"Carrier: {kind}");
            _out.WriteLine($"Raw capacity: {raw} bytes");
            _out.WriteLine($"Payload capacity: {net} bytes (after envelope{(options.ErrorCorrection > 0 ? " and error correction" : "")})");
            return ExitCodes.Success;
        }

        public int Analyze(string path)
        {
            RequireFile(path);
            using var image = Image.Load<Rgba32>(path);

            var chi = new ChiSquareAttack().Analyze(image);
            var rs = new RsAnalysis().Analyze(image);
            var spa = new SamplePairAnalysis().Analyze(image);

            _out.WriteLine($"Image: {image.Width}x{image.Height}");
            _out.WriteLine($"Chi-square embedding probability: {chi.EmbeddingProbability:F3} (statistic {chi.Statistic:F1}, {chi.DegreesOfFreedom} degrees of freedom)");
            _out.WriteLine($"RS estimated embedding rate: {Rate(rs.EstimatedEmbeddingRate)}");
            _out.WriteLine($"Sample pair estimated embedding rate: {Rate(spa.EstimatedEmbeddingRate)}");

            bool suspicious = chi.EmbeddingProbability > 0.5 || rs.EstimatedEmbeddingRate > 0.1 || spa.EstimatedEmbeddingRate > 0.1;
            _out.WriteLine(suspicious
                ? "Verdict: LSB replacement likely. These tests do not see LSB matching or transform-domain methods."
                : "Verdict: no sign of LSB replacement. These tests do not see LSB matching or transform-domain methods.");
            return ExitCodes.Success;
        }

        public int Compare(string coverPath, string stegoPath)
        {
            RequireFile(coverPath);
            RequireFile(stegoPath);

            if (CarrierKinds.Detect(CarrierKind.Auto, coverPath) == CarrierKind.Wav)
            {
                _out.WriteLine(DescribeAudio(AudioMetrics.Compare(PcmAudio.Load(coverPath), PcmAudio.Load(stegoPath))));
                return ExitCodes.Success;
            }

            using var cover = Image.Load<Rgba32>(coverPath);
            using var stego = Image.Load<Rgba32>(stegoPath);
            _out.WriteLine(DescribeImage(ImageMetrics.Compare(cover, stego)));
            return ExitCodes.Success;
        }

        // ---------- helpers ----------

        private void EmbedImage(CarrierOptions options, string outputPath, byte[] data, StegoKey key)
        {
            var pipeline = Pipeline(ImageAlgorithm(key), options);
            using var cover = Image.Load<Rgba32>(options.Input);
            long capacity = pipeline.Capacity(cover);
            try
            {
                pipeline.Embed(data, options.Input, outputPath, key);
            }
            catch (NotSupportedException e)
            {
                throw new CliException(e.Message, ExitCodes.Usage);
            }

            using var stego = Image.Load<Rgba32>(outputPath);
            _out.WriteLine($"Embedded {data.Length} bytes into {outputPath}.");
            _out.WriteLine($"Capacity {capacity} bytes, embedding rate {Rate(capacity == 0 ? 1 : (double)data.Length / capacity)}.");
            _out.WriteLine(DescribeImage(ImageMetrics.Compare(cover, stego)));
        }

        private ExtractResult ExtractImage(CarrierOptions options, StegoKey key)
        {
            using var image = Image.Load<Rgba32>(options.Input);
            return Pipeline(ImageAlgorithm(key), options).Extract(image, key);
        }

        private void EmbedInto<T>(StegoPipeline<T> pipeline, byte[] data, StegoKey key, string outputPath, T carrier, Action<T, string> save, Func<T, T> clone, Func<T, T, string> describe)
        {
            long capacity = pipeline.Capacity(carrier);
            T cover = clone == null ? default : clone(carrier);
            try
            {
                pipeline.Embed(data, carrier, key);
                save(carrier, outputPath);

                _out.WriteLine($"Embedded {data.Length} bytes into {outputPath}.");
                _out.WriteLine($"Capacity {capacity} bytes, embedding rate {Rate(capacity == 0 ? 1 : (double)data.Length / capacity)}.");
                if (cover != null && describe != null)
                    _out.WriteLine(describe(cover, carrier));
            }
            finally
            {
                (cover as IDisposable)?.Dispose();
            }
        }

        private static (long Raw, long Net) CapacityOf<T>(IStegAlgorithm<T> algorithm, CarrierOptions options, T carrier)
        {
            try
            {
                return (algorithm.Capacity(carrier), Pipeline(algorithm, options).Capacity(carrier));
            }
            finally
            {
                (carrier as IDisposable)?.Dispose();
            }
        }

        private static StegoPipeline<T> Pipeline<T>(IStegAlgorithm<T> algorithm, CarrierOptions options)
        {
            if (options.ErrorCorrection < 0 || options.ErrorCorrection == 1 || options.ErrorCorrection > 254)
                throw new CliException("--ecc must be 0 (off) or 2 to 254 parity bytes per block.", ExitCodes.Usage);

            var inner = options.ErrorCorrection > 0
                ? new ErrorCorrectedAlgorithm<T>(algorithm, new ReedSolomonCode(options.ErrorCorrection))
                : algorithm;
            return new StegoPipeline<T>(inner, new PayloadEnvelope { Compress = options.Compress });
        }

        private static IStegAlgorithm<Image<Rgba32>> ImageAlgorithm(StegoKey key) => new LSB(new KeyedPermutationSelector(key));

        private static IStegAlgorithm<TextCarrier> TextAlgorithm(TextMethod method, StegoKey key)
        {
            return method switch
            {
                TextMethod.Whitespace => new WhitespaceCoding(key),
                TextMethod.Homoglyph => new HomoglyphCoding(key),
                _ => new ZeroWidthCoding(key),
            };
        }

        private static StegoKey LoadKey(CarrierOptions options)
        {
            return TryLoadKey(options) ?? throw new CliException("A key is required: pass --passphrase or --key-file (create one with keygen).", ExitCodes.Usage);
        }

        private static StegoKey TryLoadKey(CarrierOptions options)
        {
            if (options.Passphrase != null && options.KeyFile != null)
                throw new CliException("Pass either --passphrase or --key-file, not both.", ExitCodes.Usage);
            if (options.Passphrase != null)
            {
                if (options.Passphrase.Length == 0)
                    throw new CliException("The passphrase must not be empty.", ExitCodes.Usage);
                return StegoKey.FromPassphrase(options.Passphrase);
            }
            if (options.KeyFile != null)
            {
                RequireFile(options.KeyFile);
                var bytes = File.ReadAllBytes(options.KeyFile);
                if (bytes.Length == 0)
                    throw new CliException("The key file is empty.", ExitCodes.Usage);
                return StegoKey.FromBytes(bytes);
            }
            return null;
        }

        private static void RequireFile(string path)
        {
            if (path == null)
                throw new CliException("An input file is required.", ExitCodes.Usage);
            if (!File.Exists(path))
                throw new CliException($"File not found: {path}", ExitCodes.Usage);
        }

        private static string Rate(double value) => double.IsNaN(value) ? "undefined" : $"{value * 100:F1}%";

        private static string DescribeImage(ImageComparison c)
        {
            return $"Distortion: PSNR {(double.IsPositiveInfinity(c.Psnr) ? "infinite" : $"{c.Psnr:F2} dB")}, SSIM {c.Ssim:F5}, {c.ChangedSamples} of {c.TotalSamples} samples changed ({Rate(c.ChangeRate)}), max change {c.MaxAbsoluteError}.";
        }

        private static string DescribeAudio(AudioComparison c)
        {
            return $"Distortion: SNR {(double.IsPositiveInfinity(c.Snr) ? "infinite" : $"{c.Snr:F2} dB")}, {c.ChangedSamples} of {c.TotalSamples} samples changed ({Rate(c.ChangeRate)}), max change {c.MaxAbsoluteError}.";
        }

        private static string DescribeJpeg(JpegComparison c)
        {
            return $"Distortion: {c.ChangedCoefficients} of {c.NonZeroCoefficients} non-zero coefficients changed ({Rate(c.ChangeRate)}); decoded PSNR {(double.IsPositiveInfinity(c.Pixels.Psnr) ? "infinite" : $"{c.Pixels.Psnr:F2} dB")}, SSIM {c.Pixels.Ssim:F5}.";
        }
    }
}
