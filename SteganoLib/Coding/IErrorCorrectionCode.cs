namespace SteganoLib.Coding
{
    /// <summary>Turns a payload into a longer stream that survives a bounded number of corrupted bytes.</summary>
    public interface IErrorCorrectionCode
    {
        /// <summary>Encoded length of <paramref name="dataLength"/> payload bytes.</summary>
        long EncodedLength(long dataLength);

        /// <summary>Largest payload whose encoding fits into <paramref name="encodedCapacity"/> bytes.</summary>
        long MaxDataLength(long encodedCapacity);

        byte[] Encode(byte[] data);

        /// <summary>Recover the payload; <paramref name="correctedSymbols"/> counts the bytes that were repaired.</summary>
        bool TryDecode(byte[] encoded, out byte[] data, out int correctedSymbols);
    }
}
