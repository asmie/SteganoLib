using System;

namespace SteganoLib.Payload
{
    public enum ExtractionStatus
    {
        /// <summary>Payload recovered and verified.</summary>
        Success,

        /// <summary>No envelope found. Also what a wrong key looks like when the pixel order is keyed.</summary>
        NotFound,

        /// <summary>Envelope found, but the codec rejected it: wrong key or modified data.</summary>
        AuthenticationFailed,

        /// <summary>Envelope found, but its version or codec is not known to this reader.</summary>
        Unsupported,
    }

    /// <summary>Extraction outcome. A default value represents <see cref="ExtractionStatus.NotFound"/>.</summary>
    public readonly struct ExtractResult
    {
        private ExtractResult(ExtractionStatus status, byte[] data)
        {
            _status = status;
            Data = data;
        }

        private readonly ExtractionStatus _status;

        // Keep the published enum values, including Success = 0, while making a
        // zero-initialized struct a failure. Explicit successes always have data.
        public ExtractionStatus Status => _status == ExtractionStatus.Success && Data == null
            ? ExtractionStatus.NotFound
            : _status;

        /// <summary>Recovered bytes on <see cref="ExtractionStatus.Success"/>, otherwise <c>null</c>.</summary>
        public byte[] Data { get; }

        public bool IsSuccess => Status == ExtractionStatus.Success;

        public static ExtractResult Success(byte[] data) => new(ExtractionStatus.Success, data ?? Array.Empty<byte>());

        public static ExtractResult NotFound() => new(ExtractionStatus.NotFound, null);

        public static ExtractResult AuthenticationFailed() => new(ExtractionStatus.AuthenticationFailed, null);

        public static ExtractResult Unsupported() => new(ExtractionStatus.Unsupported, null);
    }
}
