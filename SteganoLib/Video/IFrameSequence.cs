#nullable enable

using System;

namespace SteganoLib.Video
{
    /// <summary>
    /// An ordered set of frames that can be decoded one at a time, changed and stored
    /// back. Frame references are valid for the duration of the callback; do not retain
    /// or dispose them. Ownership and failed-modification behavior depend on the sequence.
    /// Keep frame count and order stable during operations, and use independent storage for embedding targets.
    /// </summary>
    /// <typeparam name="TFrame">Decoded frame type, e.g. an image or a JPEG at the coefficient level.</typeparam>
    public interface IFrameSequence<out TFrame>
    {
        int Count { get; }

        /// <summary>Provide frame <paramref name="index"/> to <paramref name="reader"/> for read-only use.</summary>
        TResult Read<TResult>(int index, Func<TFrame, TResult> reader);

        /// <summary>Let <paramref name="action"/> change frame <paramref name="index"/>. A sequence may update it in place or store it after the callback succeeds.</summary>
        void Modify(int index, Action<TFrame> action);
    }
}
