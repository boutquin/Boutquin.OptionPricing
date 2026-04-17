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
/// Theta is expressed as daily time-value decay (annualized theta / 365).
/// Rho is quoted per 1 percentage-point move in the risk-free rate.
/// </para>
/// <para>
/// Following Hull §19.12 "Extension of formulas," the effective carry
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
    public GreekSet CalculateGreeks(EuropeanOption option, OptionMarketData market)
    {
        double t = market.TimeToExpiryInYears(option.ExpiryDate);
        if (t <= 0d)
        {
            return GreekSet.Empty;
        }

        double s = (double)market.Spot;
        double k = (double)option.Strike;
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

        // Phase 5b: route N(x) through CumulativeNormal.Evaluate directly — Laikov 2025
        // exponential-free erf, ~14-digit precision, branchless (arXiv:2504.05068).
        // Pdf retains NormalDistribution.Pdf (closed-form; not delegated).
        double pdf = NormalDistribution.Pdf(d1);
        double sqrtT = Math.Sqrt(t);
        double discountedSpot = Math.Exp(-effectiveQ * t);
        double discountedStrike = Math.Exp(-r * t);

        double delta = option.CallPut switch
        {
            CallPut.Call => discountedSpot * CumulativeNormal.Evaluate(d1),
            CallPut.Put => discountedSpot * (CumulativeNormal.Evaluate(d1) - 1d),
            _ => 0d
        };

        double gamma = discountedSpot * pdf / (s * sigma * sqrtT);
        double vega = s * discountedSpot * pdf * sqrtT / 100d;

        double thetaCall =
            (-s * discountedSpot * pdf * sigma / (2d * sqrtT))
            - (r * k * discountedStrike * CumulativeNormal.Evaluate(d2))
            + (effectiveQ * s * discountedSpot * CumulativeNormal.Evaluate(d1));

        double thetaPut =
            (-s * discountedSpot * pdf * sigma / (2d * sqrtT))
            + (r * k * discountedStrike * CumulativeNormal.Evaluate(-d2))
            - (effectiveQ * s * discountedSpot * CumulativeNormal.Evaluate(-d1));

        double rho = option.CallPut switch
        {
            CallPut.Call => k * t * discountedStrike * CumulativeNormal.Evaluate(d2) / 100d,
            CallPut.Put => -k * t * discountedStrike * CumulativeNormal.Evaluate(-d2) / 100d,
            _ => 0d
        };

        double theta = (option.CallPut == CallPut.Call ? thetaCall : thetaPut) / 365d;

        return new GreekSet(delta, gamma, vega, theta, rho);
    }
}
