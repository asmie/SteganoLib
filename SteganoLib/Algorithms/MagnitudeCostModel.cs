using System;

namespace SteganoLib.Algorithms
{
    /// <summary>
    /// Coefficients of magnitude one are never changed (they would become zero and
    /// vanish from the receiver's view). Larger magnitudes are cheaper to change:
    /// cost = 1 / (|c| - 1).
    /// </summary>
    public sealed class MagnitudeCostModel : ICoefficientCostModel
    {
        public double Cost(short coefficient, int zigzagIndex)
        {
            int magnitude = Math.Abs(coefficient);
            if (magnitude <= 1)
                return double.PositiveInfinity;

            return 1.0 / (magnitude - 1);
        }
    }
}
