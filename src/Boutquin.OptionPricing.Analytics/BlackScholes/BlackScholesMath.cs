// Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
//
//   Licensed under the Apache License, Version 2.0 (the "License").
//   You may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//
//   See the License for the specific language governing permissions and
//   limitations under the License.
//

namespace Boutquin.OptionPricing.Analytics.BlackScholes;

/// <summary>
/// Shared mathematical kernels for the Black-Scholes family of pricers and Greek calculators.
/// </summary>
/// <remarks>
/// This is an internal helper; all public-facing entry points are
/// <see cref="BlackScholesPricer"/> and <see cref="BlackScholesGreeks"/>.
/// </remarks>
internal static class BlackScholesMath
{
    /// <summary>
    /// Computes the d1 and d2 terms of the Black-Scholes formula for a European option
    /// on an asset paying a continuous dividend yield.
    /// </summary>
    /// <remarks>
    /// <para>
    /// d1 = [ln(S/K) + (r - q + sigma^2 / 2) * T] / (sigma * sqrt(T))
    /// </para>
    /// <para>
    /// d2 = d1 - sigma * sqrt(T)
    /// </para>
    /// <para>
    /// These values feed directly into the cumulative normal distribution to produce
    /// risk-neutral probabilities used in pricing and Greek calculations.
    /// </para>
    /// </remarks>
    /// <param name="spot">Current underlying spot price (S).</param>
    /// <param name="strike">Option strike price (K).</param>
    /// <param name="rate">Continuously compounded risk-free rate (r).</param>
    /// <param name="dividendYield">Continuously compounded dividend yield (q).</param>
    /// <param name="volatility">Annualized implied volatility (sigma).</param>
    /// <param name="timeToExpiry">Time to expiry in years (T). Must be positive.</param>
    /// <returns>
    /// A tuple <c>(D1, D2)</c> containing the two standard-normal arguments
    /// used throughout the Black-Scholes pricing and Greeks formulas.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="spot"/>, <paramref name="strike"/>,
    /// <paramref name="volatility"/>, or <paramref name="timeToExpiry"/> is not strictly
    /// positive. d1/d2 are undefined (NaN/∞) for those inputs; the guard makes the
    /// precondition explicit at the math seam rather than producing silent NaN downstream.
    /// </exception>
    public static (double D1, double D2) ComputeD1D2(double spot, double strike, double rate, double dividendYield, double volatility, double timeToExpiry)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strike);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(volatility);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeToExpiry);
        if (!double.IsFinite(spot) || !double.IsFinite(strike) || !double.IsFinite(rate)
            || !double.IsFinite(dividendYield) || !double.IsFinite(volatility) || !double.IsFinite(timeToExpiry))
        {
            throw new ArgumentOutOfRangeException(nameof(spot), "Black-Scholes inputs must be finite.");
        }

        double sigmaSqrtT = volatility * Math.Sqrt(timeToExpiry);
        if (!double.IsFinite(volatility * volatility) || sigmaSqrtT == 0d)
        {
            // Preserve the ordinary arithmetic path and its numerical baselines.
            // Scaling before squaring avoids inf-inf in d2 at finite high vol,
            // and dividing before multiplying retains the zero-volatility limit.
            double sqrtT = Math.Sqrt(timeToExpiry);
            double carry = rate - dividendYield;
            double scaledCarry = double.IsFinite(carry) ? carry / volatility : rate / volatility - dividendYield / volatility;
            double centre = Math.Log(spot / strike) / volatility / sqrtT + scaledCarry * sqrtT;
            double halfWidth = 0.5d * volatility * sqrtT;
            return (centre + halfWidth, centre - halfWidth);
        }

        double d1 = (Math.Log(spot / strike) + ((rate - dividendYield + (0.5d * volatility * volatility)) * timeToExpiry)) / sigmaSqrtT;
        double d2 = d1 - sigmaSqrtT;
        return (d1, d2);
    }
}
