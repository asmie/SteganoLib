using System;

using SteganoLib.Payload;
using Xunit;

namespace SteganoLib.Test
{
    public class ExtractResultTests
    {
        [Fact]
        public void DefaultResultsAreNotFound()
        {
            foreach (var result in new[] { default(ExtractResult), new ExtractResult(), (new ExtractResult[1])[0] })
            {
                Assert.Equal(ExtractionStatus.NotFound, result.Status);
                Assert.False(result.IsSuccess);
                Assert.Null(result.Data);
            }
        }

        [Fact]
        public void ExplicitSuccessCanContainAnEmptyPayload()
        {
            foreach (var payload in new[] { null, Array.Empty<byte>(), new byte[] { 1, 2, 3 } })
            {
                var result = ExtractResult.Success(payload);
                Assert.Equal(ExtractionStatus.Success, result.Status);
                Assert.True(result.IsSuccess);
                Assert.Equal(payload ?? Array.Empty<byte>(), result.Data);
            }
        }

        [Fact]
        public void FailureFactoriesKeepTheirStatusesAndHaveNoData()
        {
            var cases = new[]
            {
                (ExtractResult.NotFound(), ExtractionStatus.NotFound),
                (ExtractResult.AuthenticationFailed(), ExtractionStatus.AuthenticationFailed),
                (ExtractResult.Unsupported(), ExtractionStatus.Unsupported),
            };
            foreach (var (result, status) in cases)
            {
                Assert.Equal(status, result.Status);
                Assert.False(result.IsSuccess);
                Assert.Null(result.Data);
            }
        }
    }
}
