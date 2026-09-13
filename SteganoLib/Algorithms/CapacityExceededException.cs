using System;

namespace SteganoLib.Algorithms
{
    /// <summary>Thrown when a payload does not fit into the carrier.</summary>
    public class CapacityExceededException : InvalidOperationException
    {
        public CapacityExceededException(long required, long available)
            : base($"Payload needs {required} bytes but the carrier holds {available}.")
        {
            Required = required;
            Available = available;
        }

        public long Required { get; }

        public long Available { get; }
    }
}
