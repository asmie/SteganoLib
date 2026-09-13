using System.Collections.Generic;

namespace SteganoLib.Selection
{
    /// <summary>Decides which sample indices carry payload bits and in what order. Must not repeat an index.</summary>
    public interface ISampleSelector
    {
        IEnumerable<long> Indices(long count);
    }
}
