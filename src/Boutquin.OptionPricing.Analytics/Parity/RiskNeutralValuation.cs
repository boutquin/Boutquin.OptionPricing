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

using Boutquin.OptionPricing.Domain.Market;

namespace Boutquin.OptionPricing.Analytics.Parity;

/// <summary>
/// Computes the risk-neutral forward price of an underlying asset, accounting for
/// risk-free discounting, continuous dividend yield, and borrow cost.
/// </summary>
/// <remarks>
/// The forward price under risk-neutral valuation is
/// <c>F = S * exp((r - q - b) * T)</c>, where <c>q</c> is the dividend yield,
/// <c>r</c> is the risk-free rate, and <c>b</c> is the borrow cost. The borrow
/// cost behaves like an additional yield that reduces the forward, consistent with
/// the pricer's <c>effectiveQ = q + b</c>. Expressed in terms of pre-computed
/// discount factors this is <c>F = S * D_div * D_borrow / D_rf</c>.
/// </remarks>
public static class RiskNeutralValuation
{
    /// <summary>
    /// Computes the risk-neutral forward price from pre-computed discount factors.
    /// </summary>
    /// <param name="spot">Current underlying spot price.</param>
    /// <param name="discountFactor">Risk-free discount factor exp(-r*T) to expiry.</param>
    /// <param name="dividendDiscountFactor">
    /// Dividend discount factor exp(-q*T). Defaults to 1 (no dividends).
    /// </param>
    /// <param name="borrowDiscountFactor">
    /// Borrow-cost discount factor exp(-b*T). Defaults to 1 (no borrow cost).
    /// </param>
    /// <returns>
    /// The forward price <c>S * D_div * D_borrow / D_rf</c>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Spot or a supplied factor is not finite and positive.</exception>
    /// <exception cref="ArithmeticException">The forward cannot be represented as a finite positive double.</exception>
    public static double ForwardPrice(double spot, double discountFactor, double dividendDiscountFactor = 1d, double borrowDiscountFactor = 1d)
    {
        ValidateFactor(spot, nameof(spot));
        ValidateFactor(discountFactor, nameof(discountFactor));
        ValidateFactor(dividendDiscountFactor, nameof(dividendDiscountFactor));
        ValidateFactor(borrowDiscountFactor, nameof(borrowDiscountFactor));
        double forward = spot * dividendDiscountFactor * borrowDiscountFactor / discountFactor;
        if (!double.IsFinite(forward) || forward <= 0d)
        {
            throw new ArithmeticException("The forward price is outside the finite positive numeric range.");
        }

        return forward;
    }

    /// <summary>
    /// Computes the risk-neutral forward price by reading discount factors from the
    /// market curves at the given expiry date.
    /// </summary>
    /// <param name="market">Market snapshot supplying discount, dividend, and borrow curves.</param>
    /// <param name="expiryDate">Option expiry date used to read curve discount factors.</param>
    /// <returns>
    /// The forward price derived from the market's curve-based discount factors.
    /// </returns>
    /// <exception cref="ArgumentNullException">The market snapshot is null.</exception>
    public static double ForwardPrice(OptionMarketData market, DateOnly expiryDate)
    {
        ArgumentNullException.ThrowIfNull(market);
        return market.ForwardPrice(expiryDate);
    }

    private static void ValidateFactor(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0d)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and positive.");
        }
    }
}
