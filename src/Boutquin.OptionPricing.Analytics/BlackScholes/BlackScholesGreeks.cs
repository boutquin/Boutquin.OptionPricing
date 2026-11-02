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

using Boutquin.Domain.Exceptions;
using Boutquin.Numerics.Distributions;
using Boutquin.OptionPricing.Abstractions;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Domain.Results;

namespace Boutquin.OptionPricing.Analytics.BlackScholes;

/// <summary>
/// Computes the first-order Greeks (delta, gamma, vega, theta, rho) for European options
/// under the Generalized Black-Scholes model with continuous dividend yield.
/// </summary>
/// <remarks>
/// <para>
/// Delta measures the sensitivity of option price to a unit move in spot.
/// Gamma is the second derivative of price with respect to spot.
/// Vega is quoted per 1 percentage-point move in implied volatility.
/// Theta is expressed as daily time-value decay using the fixed market convention
/// of annualized theta / 365 calendar days (not tied to the discount curve day count).
/// Rho is quoted per 1 percentage-point move in the risk-free rate.
/// </para>
/// <para>
/// Following Hull section 19.12 "Extension of formulas," the effective carry
/// q_eff = q + b (dividend yield plus borrow rate) is used symmetrically in
/// d1, d2, every e^{-q T} discount factor, and the q S N(d1) drift term in
/// theta — matching the pricer so the Greeks are the exact partial derivatives
/// of <see cref="BlackScholesPricer"/>'s output.
/// </para>
/// <para>
/// When time to expiry is zero or negative, all Greeks collapse to
/// <see cref="GreekSet.Empty"/> because the option has no remaining time value.
/// </para>
/// </remarks>
public sealed class BlackScholesGreeks : IGreekProvider<EuropeanOption, OptionMarketData, GreekSet>
{
    /// <summary>
    /// Calculates delta, gamma, vega, theta, and rho for a European option under the
    /// supplied market snapshot using the closed-form Black-Scholes expressions.
    /// </summary>
    /// <param name="option">European option contract specifying strike, expiry, and call/put flag.</param>
    /// <param name="market">
    /// Market snapshot containing spot price, discount curve, dividend curve, and flat volatility.
    /// </param>
    /// <returns>
    /// A <see cref="GreekSet"/> with delta, gamma, vega, theta (per calendar day), and rho
    /// (per percentage point). Returns <see cref="GreekSet.Empty"/> when the option has expired.
    /// </returns>
    /// <exception cref="ArgumentNullException">The option or market is null.</exception>
    /// <exception cref="CurrencyMismatchException">Option and spot currencies differ.</exception>
    public GreekSet CalculateGreeks(EuropeanOption option, OptionMarketData market)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(market);
        if (market.Spot.Currency != option.Currency)
        {
            throw new CurrencyMismatchException(option.Currency, market.Spot.Currency);
        }

        double t = market.TimeToExpiryInYears(option.ExpiryDate);
        if (t <= 0d)
        {
            return GreekSet.Empty;
        }

        double s = (double)market.Spot.Amount;
        double k = (double)option.Strike.Amount;
        double r = market.RiskFreeRate(option.ExpiryDate);
        double q = market.DividendYield(option.ExpiryDate);
        double b = market.BorrowRate(option.ExpiryDate);
        double sigma = market.FlatVolatility;

        // Hull §19.12: one carry parameter, symmetric everywhere. Borrow reduces
        // the forward like an additional yield, so the effective carry feeding
        // d1, d2, every e^{-qT} factor, and the qS N(d1) drift term in theta is
        // q + b — identical to BlackScholesPricer. Any asymmetry would mean the
        // Greeks are partial derivatives of a different pricing function than
        // the one the pricer evaluates.
        double effectiveQ = q + b;

        (double d1, double d2) = BlackScholesMath.ComputeD1D2(s, k, r, effectiveQ, sigma, t);

        // Phase 5b: route N(x) through CumulativeNormal<double>.Evaluate directly — Laikov 2025
        // exponential-free erf, ~14-digit precision, branchless (arXiv:2504.05068).
        // Pdf retains NormalDistribution.Pdf (closed-form; not delegated).
        double pdf = NormalDistribution<double>.Pdf(d1);
        double sqrtT = Math.Sqrt(t);

        // carryDiscount and rateDiscount are discount FACTORS (e^{-qT}, e^{-rT}),
        // not discounted prices — distinct from the pricer's local named discountedSpot.
        double carryDiscount = Math.Exp(-effectiveQ * t);
        double rateDiscount = Math.Exp(-r * t);

        // Hoist the four standard-normal CDF evaluations used across the Greeks.
        // -d1/-d2 are evaluated directly (not 1 - N(d)) to preserve tail precision.
        double nd1 = CumulativeNormal<double>.Evaluate(d1);
        double nd2 = CumulativeNormal<double>.Evaluate(d2);
        double nMinusD1 = CumulativeNormal<double>.Evaluate(-d1);
        double nMinusD2 = CumulativeNormal<double>.Evaluate(-d2);

        double delta = option.CallPut switch
        {
            CallPut.Call => carryDiscount * nd1,
            CallPut.Put => carryDiscount * (nd1 - 1d),
            _ => throw new NotSupportedException("Unsupported call/put flag.")
        };

        double gamma = carryDiscount * pdf / (s * sigma * sqrtT);
        double vega = s * carryDiscount * pdf * sqrtT / 100d;

        double thetaCall =
            (-s * carryDiscount * pdf * sigma / (2d * sqrtT))
            - (r * k * rateDiscount * nd2)
            + (effectiveQ * s * carryDiscount * nd1);

        double thetaPut =
            (-s * carryDiscount * pdf * sigma / (2d * sqrtT))
            + (r * k * rateDiscount * nMinusD2)
            - (effectiveQ * s * carryDiscount * nMinusD1);

        double rho = option.CallPut switch
        {
            CallPut.Call => k * t * rateDiscount * nd2 / 100d,
            CallPut.Put => -k * t * rateDiscount * nMinusD2 / 100d,
            _ => throw new NotSupportedException("Unsupported call/put flag.")
        };

        // Theta is per calendar day by convention: annualized theta / 365, a fixed
        // market convention independent of the discount curve's day count.
        double theta = (option.CallPut == CallPut.Call ? thetaCall : thetaPut) / 365d;

        return new GreekSet(delta, gamma, vega, theta, rho);
    }
}
