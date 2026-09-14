using System;

namespace SteganoLib.Video
{
    /// <summary>
    /// An ordered set of frames that can be decoded one at a time, changed and stored
    /// back. The sequence owns each decoded frame: callers work on it inside the
    /// callback and never keep a reference.
    /// </summary>
    /// <typeparam name="TFrame">Decoded frame type, e.g. an image or a JPEG at the coefficient level.</typeparam>
    public interface IFrameSequence<out TFrame>
    {
        int Count { get; }

        /// <summary>Decode frame <paramref name="index"/> and hand it to <paramref name="reader"/>; nothing is stored back.</summary>
        TResult Read<TResult>(int index, Func<TFrame, TResult> reader);

        /// <summary>Decode frame <paramref name="index"/>, let <paramref name="action"/> change it, then store it back.</summary>
        void Modify(int index, Action<TFrame> action);
    }
}
