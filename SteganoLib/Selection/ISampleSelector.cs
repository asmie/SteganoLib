using System.Collections.Generic;
using System.Linq;

namespace SteganoLib.Selection
{
    /// <summary>
    /// Decides which sample indices carry payload bits and in what order. May select a
    /// subset, but must terminate, stay in [0, count) and never repeat an index.
    /// Calls with the same configuration and count must return the same sequence.
    /// </summary>
    public interface ISampleSelector
    {
        IEnumerable<long> Indices(long count);

        /// <summary>Exact number of selected samples; override to avoid enumerating when the count is known.</summary>
        long Count(long count) => Indices(count).LongCount();
    }
}
