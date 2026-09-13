using System;

namespace SteganoLib.Algorithms
{
    /// <summary>Colour channels an image algorithm is allowed to modify. Alpha is never an option.</summary>
    [Flags]
    public enum ColorChannels
    {
        None = 0,
        Red = 1,
        Green = 2,
        Blue = 4,
        All = Red | Green | Blue,
    }
}
