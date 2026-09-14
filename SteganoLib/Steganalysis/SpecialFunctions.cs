using System;

namespace SteganoLib.Steganalysis
{
    /// <summary>Gamma-family functions needed for chi-square p-values.</summary>
    internal static class SpecialFunctions
    {
        private const double Epsilon = 1e-14;
        private const int MaxIterations = 10_000;

        private static readonly double[] LanczosCoefficients =
        {
            0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
            -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6,
            1.5056327351493116e-7,
        };

        /// <summary>Natural logarithm of the gamma function for positive arguments (Lanczos approximation).</summary>
        public static double LogGamma(double x)
        {
            if (x <= 0)
                throw new ArgumentOutOfRangeException(nameof(x));

            if (x < 0.5)
                return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - LogGamma(1 - x);

            x -= 1;
            double a = LanczosCoefficients[0];
            double t = x + 7.5;
            for (int i = 1; i < LanczosCoefficients.Length; i++)
                a += LanczosCoefficients[i] / (x + i);

            return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(a);
        }

        /// <summary>Regularised lower incomplete gamma function P(a, x).</summary>
        public static double RegularizedGammaP(double a, double x)
        {
            if (a <= 0) throw new ArgumentOutOfRangeException(nameof(a));
            if (x < 0) throw new ArgumentOutOfRangeException(nameof(x));
            if (x == 0) return 0;

            return x < a + 1 ? SeriesP(a, x) : 1 - ContinuedFractionQ(a, x);
        }

        /// <summary>Cumulative distribution function of the chi-square distribution.</summary>
        public static double ChiSquareCdf(double statistic, int degreesOfFreedom)
        {
            if (degreesOfFreedom < 1) throw new ArgumentOutOfRangeException(nameof(degreesOfFreedom));
            if (statistic <= 0) return 0;
            return RegularizedGammaP(degreesOfFreedom / 2.0, statistic / 2);
        }

        private static double SeriesP(double a, double x)
        {
            double sum = 1 / a, term = sum, ap = a;
            for (int n = 0; n < MaxIterations; n++)
            {
                ap += 1;
                term *= x / ap;
                sum += term;
                if (Math.Abs(term) < Math.Abs(sum) * Epsilon)
                    break;
            }
            return sum * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
        }

        private static double ContinuedFractionQ(double a, double x)
        {
            const double tiny = 1e-300;
            double b = x + 1 - a;
            double c = 1 / tiny;
            double d = 1 / b;
            double h = d;
            for (int i = 1; i < MaxIterations; i++)
            {
                double an = -i * (i - a);
                b += 2;
                d = an * d + b;
                if (Math.Abs(d) < tiny) d = tiny;
                c = b + an / c;
                if (Math.Abs(c) < tiny) c = tiny;
                d = 1 / d;
                double delta = d * c;
                h *= delta;
                if (Math.Abs(delta - 1) < Epsilon)
                    break;
            }
            return Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
        }
    }
}
