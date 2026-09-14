using System;

namespace SteganoLib.Cli
{
    /// <summary>A failure to report to the user with a specific exit code.</summary>
    internal sealed class CliException : Exception
    {
        public CliException(string message, int exitCode = ExitCodes.Failure)
            : base(message)
        {
            ExitCode = exitCode;
        }

        public int ExitCode { get; }
    }

    internal static class ExitCodes
    {
        /// <summary>The operation succeeded.</summary>
        public const int Success = 0;

        /// <summary>The operation ran but did not succeed: wrong key, no payload, payload too large, bad file.</summary>
        public const int Failure = 1;

        /// <summary>The arguments were wrong.</summary>
        public const int Usage = 2;
    }
}
