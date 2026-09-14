using BenchmarkDotNet.Running;

namespace SteganoLib.Benchmarks
{
    /// <summary>
    /// Run with <c>dotnet run -c Release --project SteganoLib.Benchmarks -- --filter '*'</c>.
    /// Add <c>--job short</c> for a quick look, or a class name in the filter such as <c>*Lsb*</c>.
    /// </summary>
    public static class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
