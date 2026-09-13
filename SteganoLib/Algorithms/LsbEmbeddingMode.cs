namespace SteganoLib.Algorithms
{
    public enum LsbEmbeddingMode
    {
        /// <summary>Overwrite the least significant bit. Fast, but detectable by the chi-square attack.</summary>
        Replace,

        /// <summary>Move the value by plus or minus one at random when the bit differs. Preferred.</summary>
        Match,
    }
}
