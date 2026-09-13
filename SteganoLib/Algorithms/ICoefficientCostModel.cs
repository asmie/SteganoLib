namespace SteganoLib.Algorithms
{
    /// <summary>Price of moving one quantised DCT coefficient one step toward zero.</summary>
    public interface ICoefficientCostModel
    {
        /// <summary>Cost of changing a non-zero AC coefficient at zigzag position 1 to 63.</summary>
        /// <returns>Non-negative cost; <see cref="double.PositiveInfinity"/> forbids the change.</returns>
        double Cost(short coefficient, int zigzagIndex);
    }
}
