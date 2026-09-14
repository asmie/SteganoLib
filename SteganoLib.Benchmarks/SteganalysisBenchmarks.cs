using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Quality;
using SteganoLib.Steganalysis;

namespace SteganoLib.Benchmarks
{
    [MemoryDiagnoser]
    public class SteganalysisBenchmarks
    {
        private Image<Rgba32> _image;
        private Image<Rgba32> _other;

        [GlobalSetup]
        public void Setup()
        {
            _image = Covers.Picture(1024, 768, 1);
            _other = Covers.Picture(1024, 768, 2);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _image.Dispose();
            _other.Dispose();
        }

        [Benchmark(Description = "Chi-square attack")]
        public ChiSquareResult ChiSquare() => new ChiSquareAttack().Analyze(_image);

        [Benchmark(Description = "RS analysis")]
        public RsResult Rs() => new RsAnalysis().Analyze(_image);

        [Benchmark(Description = "Sample pair analysis")]
        public SamplePairResult SamplePairs() => new SamplePairAnalysis().Analyze(_image);

        [Benchmark(Description = "PSNR and SSIM")]
        public ImageComparison Compare() => ImageMetrics.Compare(_image, _other);
    }
}
