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
/// Closed-form Black-Scholes pricer for European options on a dividend-paying underlying.
/// Computes option fair value using the Generalized Black-Scholes formula with continuous
/// dividend yield, flat volatility, and curve-derived risk-free discounting.
/// </summary>
/// <remarks>
/// <para>
/// The call price is <c>S * exp(-q*T) * N(d1) - K * exp(-r*T) * N(d2)</c>, and the put
/// price follows by put-call parity. When time to expiry is zero or negative, the pricer
/// returns intrinsic value with no Greeks.
/// </para>
/// <para>
/// Diagnostic metadata (d1, d2, sigma, curve types) is attached to every
/// <see cref="PriceResult"/> for downstream audit and implied-volatility solvers.
/// </para>
/// </remarks>
public sealed class BlackScholesPricer : IOptionPricer<EuropeanOption, OptionMarketData, PriceResult>
{
    /// <summary>
    /// Computes the Black-Scholes fair value for a European option under the supplied market snapshot.
    /// </summary>
    /// <param name="option">European option contract specifying strike, expiry, and call/put flag.</param>
    /// <param name="market">
    /// Market snapshot containing spot price, discount curve, dividend curve, and flat volatility.
    /// </param>
    /// <returns>
    /// A <see cref="PriceResult"/> carrying the theoretical price, diagnostic metadata
    /// (d1, d2, sigma, curve types), and a <see cref="CalculationStatus.Success"/> status.
    /// When the option has expired, the result contains intrinsic value with no Greeks.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when <paramref name="option"/> has a <c>CallPut</c> value that is neither
    /// <see cref="CallPut.Call"/> nor <see cref="CallPut.Put"/>.
    /// </exception>
    public PriceResult Price(EuropeanOption option, OptionMarketData market)
    {
        double t = market.TimeToExpiryInYears(option.ExpiryDate);
        if (t <= 0d)
        {
            return new PriceResult(option.IntrinsicValue(market.Spot), GreekSet.Empty, CalculationStatus.Success, null, nameof(BlackScholesPricer));
        }

        double s = (double)market.Spot;
        double k = (double)option.Strike;
        double r = market.RiskFreeRate(option.ExpiryDate);
        double q = market.DividendYield(option.ExpiryDate);
        double b = market.BorrowRate(option.ExpiryDate);
        double sigma = market.FlatVolatility;

        // Cost-of-carry: borrow cost reduces the forward (like an additional yield).
        double effectiveQ = q + b;

        (double d1, double d2) = BlackScholesMath.ComputeD1D2(s, k, r, effectiveQ, sigma, t);

        double discountedSpot = s * Math.Exp(-effectiveQ * t);
        double discountedStrike = k * Math.Exp(-r * t);

        // Phase 5b: route N(d1), N(d2) through CumulativeNormal.Evaluate directly —
        // Laikov 2025 exponential-free erf, ~14-digit precision, branchless
        // (arXiv:2504.05068). Equivalent to NormalDistribution.Cdf today but pins the
        // call site to the high-precision implementation.
        double price = option.CallPut switch
        {
            CallPut.Call => (discountedSpot * CumulativeNormal.Evaluate(d1)) - (discountedStrike * CumulativeNormal.Evaluate(d2)),
            CallPut.Put => (discountedStrike * CumulativeNormal.Evaluate(-d2)) - (discountedSpot * CumulativeNormal.Evaluate(-d1)),
            _ => throw new NotSupportedException("Unsupported call/put flag.")
        };

        return new PriceResult(
            Convert.ToDecimal(price),
            null,
            CalculationStatus.Success,
            new Dictionary<string, object?>
            {
                ["d1"] = d1,
                ["d2"] = d2,
                ["sigma"] = sigma,
                ["borrowRate"] = b,
                ["discountCurveType"] = market.DiscountCurve.GetType().Name,
                ["dividendCurveType"] = market.DividendCurve?.GetType().Name,
                ["borrowCurveType"] = market.BorrowCurve?.GetType().Name
            },
            nameof(BlackScholesPricer),
            "Flat volatility, curve-derived discounting, optional dividend and borrow curves, European exercise.");
    }
}
