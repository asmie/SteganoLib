#nullable enable

using System;
using System.Collections.Generic;

namespace SteganoLib.Video
{
    /// <summary>
    /// A live view of frames already held in memory. The caller owns the list and frames;
    /// keep the list stable during operations. Callbacks act on the original objects,
    /// so changes remain visible even if a callback throws. Embedding targets must not share storage.
    /// </summary>
    public sealed class FrameList<TFrame> : IFrameSequence<TFrame>
    {
        private readonly IList<TFrame> _frames;

        public FrameList(IList<TFrame> frames)
        {
            _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        }

        public int Count => _frames.Count;

        public TFrame this[int index] => Frame(index);

        public TResult Read<TResult>(int index, Func<TFrame, TResult> reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            return reader(Frame(index));
        }

        public void Modify(int index, Action<TFrame> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            action(Frame(index));
        }

        private TFrame Frame(int index)
        {
            if (index < 0 || index >= _frames.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            var frame = _frames[index];
            if (frame == null)
                throw new InvalidOperationException($"Frame {index} is null.");
            return frame;
        }
    }
}
