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
using Boutquin.Domain.Extensions;
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
    /// <exception cref="CurrencyMismatchException">
    /// Thrown when <paramref name="market"/>'s spot currency differs from
    /// <paramref name="option"/>'s contract currency.
    /// </exception>
    /// <exception cref="ArgumentNullException">The option or market is null.</exception>
    public PriceResult Price(EuropeanOption option, OptionMarketData market)
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
            // Greeks are null (not GreekSet.Empty) for the "no time value" result, matching
            // the live pricing path below which also passes null for Greeks.
            return new PriceResult(option.IntrinsicValue(market.Spot), null, CalculationStatus.Success, null, nameof(BlackScholesPricer));
        }

        double s = (double)market.Spot.Amount;
        double k = (double)option.Strike.Amount;
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
            CallPut.Call => (discountedSpot * CumulativeNormal<double>.Evaluate(d1)) - (discountedStrike * CumulativeNormal<double>.Evaluate(d2)),
            CallPut.Put => (discountedStrike * CumulativeNormal<double>.Evaluate(-d2)) - (discountedSpot * CumulativeNormal<double>.Evaluate(-d1)),
            _ => throw new NotSupportedException("Unsupported call/put flag.")
        };

        // Diagnostics allocation: this builds a Dictionary<string, object?> and boxes the
        // four double values (d1, d2, sigma, borrowRate) on every Price call. It is emitted
        // unconditionally because implied-volatility solvers and audit consumers read d1/d2/
        // sigma back through the IDiagnosticSource contract, which is typed as
        // IReadOnlyDictionary<string, object?> — the boxing is structurally forced by that
        // interface, not incidental. This is NOT a marked hot path today. If a profile ever
        // shows it hot (e.g. an inner IV-solver loop), the fix is to defer diagnostics
        // construction behind a factory so the dictionary is built only when GetDiagnostics()
        // is actually called, rather than to drop the diagnostics here.
        return new PriceResult(
            option.Currency.Amount(Convert.ToDecimal(price)),
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
