using System.IO;
using Xunit;

namespace SteganoLib.Test
{
    /// <summary>The samples must keep working as the API evolves; run them end to end.</summary>
    public class SamplesTests
    {
        [Fact]
        public void AllSamples_RunAndReportSuccess()
        {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var output = new StringWriter();
            try
            {
                Samples.Program.Run(output, dir);
                string text = output.ToString();

                Assert.Contains("PNG: extracted \"Meet at the usual place, 21:00.\" (Success)", text);
                Assert.Contains("with the wrong key: AuthenticationFailed", text);
                Assert.Contains("JPEG: extracted \"F5 lives in the DCT domain.\"", text);
                Assert.Contains("visible text unchanged: True", text);
                Assert.Contains("Text: extracted \"zw\"", text);
                Assert.Contains("pixels identical: True", text);
                Assert.Contains("payload Success", text);
                Assert.Contains("two of three images give \"split three ways\"; one alone gives NotFound", text);
                Assert.Contains("Steganalysis: chi-square probability", text);
                Assert.True(File.Exists(Path.Combine(dir, "stego.png")));
                Assert.True(File.Exists(Path.Combine(dir, "stego.jpg")));
                Assert.True(File.Exists(Path.Combine(dir, "share2.png")));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }
    }
}
