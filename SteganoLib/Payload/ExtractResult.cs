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

    public readonly struct ExtractResult
    {
        private ExtractResult(ExtractionStatus status, byte[] data)
        {
            Status = status;
            Data = data;
        }

        public ExtractionStatus Status { get; }

        /// <summary>Recovered bytes on <see cref="ExtractionStatus.Success"/>, otherwise <c>null</c>.</summary>
        public byte[] Data { get; }

        public bool IsSuccess => Status == ExtractionStatus.Success;

        public static ExtractResult Success(byte[] data) => new(ExtractionStatus.Success, data ?? Array.Empty<byte>());

        public static ExtractResult NotFound() => new(ExtractionStatus.NotFound, null);

        public static ExtractResult AuthenticationFailed() => new(ExtractionStatus.AuthenticationFailed, null);

        public static ExtractResult Unsupported() => new(ExtractionStatus.Unsupported, null);
    }
}
