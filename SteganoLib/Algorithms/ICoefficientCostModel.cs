namespace SteganoLib.Algorithms
{
    /// <summary>
    /// Price of moving one quantised DCT payload coefficient one step toward zero.
    /// F5 always forbids magnitude-one payload changes, regardless of custom costs.
    /// The plain algorithm header does not consult this model and can shrink coefficients.
    /// </summary>
    public interface ICoefficientCostModel
    {
        /// <summary>Cost of changing a non-zero AC coefficient at zigzag position 1 to 63.</summary>
        /// <returns>Non-negative cost; <see cref="double.PositiveInfinity"/> forbids the change.</returns>
        double Cost(short coefficient, int zigzagIndex);
    }
}
