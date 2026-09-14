using System;

namespace SteganoLib.Algorithms
{
    /// <summary>
    /// Thrown when length or embedding constraints prevent a payload from fitting.
    /// Required can be less than Available when trellis change costs make embedding infeasible.
    /// </summary>
    public class CapacityExceededException : InvalidOperationException
    {
        public CapacityExceededException(long required, long available)
            : base(required > available
                ? $"Payload needs {required} bytes but the carrier holds {available}."
                : $"The payload cannot be embedded with the current framing or change constraints ({required} bytes, capacity budget {available}).")
        {
            Required = required;
            Available = available;
        }

        public long Required { get; }

        public long Available { get; }
    }
}
