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

using Boutquin.Curves.Abstractions.Curves;
using Boutquin.OptionPricing.Domain.Market;

namespace Boutquin.OptionPricing.Trees.Binomial;

/// <summary>
/// Immutable set of market and contract parameters required to build a binomial tree.
/// Encapsulates spot, strike, term structure curves, volatility, and step count,
/// and exposes derived quantities (time to expiry, continuously compounded rates)
/// consumed by tree builders.
/// </summary>
/// <param name="Spot">Current underlying asset price.</param>
/// <param name="Strike">Option strike price.</param>
/// <param name="ValuationDate">As-of date for the pricing run.</param>
/// <param name="ExpiryDate">Option expiration date.</param>
/// <param name="DiscountCurve">Risk-free discount curve used to derive the continuously compounded rate and day-count fraction.</param>
/// <param name="Volatility">Annualized volatility of the underlying, expressed as a decimal (e.g., 0.20 for 20%).</param>
/// <param name="Steps">Number of time steps in the binomial tree; controls convergence to the continuous limit.</param>
/// <param name="DividendCurve">Optional continuous dividend yield curve. When <see langword="null"/>, dividend yield is zero.</param>
/// <param name="BorrowCurve">Optional stock-borrow cost curve. When <see langword="null"/>, borrow rate is zero.</param>
public sealed record TreeModelInput(
    decimal Spot,
    decimal Strike,
    DateOnly ValuationDate,
    DateOnly ExpiryDate,
    IDiscountCurve DiscountCurve,
    double Volatility,
    int Steps,
    IDiscountCurve? DividendCurve = null,
    IDiscountCurve? BorrowCurve = null)
{
    /// <summary>
    /// Creates a <see cref="TreeModelInput"/> by extracting market data from an
    /// <see cref="OptionMarketData"/> instance, avoiding manual parameter duplication.
    /// </summary>
    /// <param name="market">The market data snapshot to extract curves and spot from.</param>
    /// <param name="strike">The option strike price.</param>
    /// <param name="expiryDate">The option expiry date.</param>
    /// <param name="steps">Number of time steps in the binomial tree.</param>
    /// <returns>A new <see cref="TreeModelInput"/> with curves and parameters from the market snapshot.</returns>
    public static TreeModelInput FromMarketData(
        OptionMarketData market,
        decimal strike,
        DateOnly expiryDate,
        int steps)
    {
        ArgumentNullException.ThrowIfNull(market);
        return new TreeModelInput(
            market.Spot.Amount,
            strike,
            market.ValuationDate,
            expiryDate,
            market.DiscountCurve,
            market.FlatVolatility,
            steps,
            market.DividendCurve,
            market.BorrowCurve);
    }

    /// <summary>
    /// Year fraction from <see cref="ValuationDate"/> to <see cref="ExpiryDate"/>
    /// computed with the day-count convention embedded in <see cref="DiscountCurve"/>.
    /// </summary>
    /// <returns>Time to expiry expressed in years.</returns>
    public double TimeToExpiryInYears()
    {
        ArgumentNullException.ThrowIfNull(DiscountCurve);
        return DiscountCurve.DayCount.YearFraction(ValuationDate, ExpiryDate);
    }

    /// <summary>
    /// Continuously compounded risk-free rate implied by the discount factor
    /// from <see cref="DiscountCurve"/> at the expiry date.
    /// </summary>
    /// <remarks>
    /// Computed as <c>r = -ln(DF) / T</c> where DF is the discount factor to expiry
    /// and T is time to expiry in years. Returns zero when time to expiry is non-positive.
    /// </remarks>
    /// <returns>Annualized continuously compounded risk-free rate.</returns>
    public double RiskFreeRate()
    {
        return ImpliedContinuousRate(DiscountCurve.DiscountFactor(ExpiryDate), TimeToExpiryInYears());
    }

    /// <summary>
    /// Continuously compounded dividend yield implied by the <see cref="DividendCurve"/>.
    /// Returns zero when no dividend curve is supplied.
    /// </summary>
    /// <returns>Annualized continuously compounded dividend yield.</returns>
    public double DividendYield()
    {
        if (DividendCurve is null)
        {
            return 0d;
        }

        return ImpliedContinuousRate(DividendCurve.DiscountFactor(ExpiryDate), TimeToExpiryInYears());
    }

    /// <summary>
    /// Continuously compounded stock-borrow cost implied by the <see cref="BorrowCurve"/>.
    /// Returns zero when no borrow curve is supplied.
    /// </summary>
    /// <returns>Annualized continuously compounded borrow rate.</returns>
    public double BorrowRate()
    {
        if (BorrowCurve is null)
        {
            return 0d;
        }

        return ImpliedContinuousRate(BorrowCurve.DiscountFactor(ExpiryDate), TimeToExpiryInYears());
    }

    private static double ImpliedContinuousRate(double discountFactor, double timeInYears)
    {
        if (!double.IsFinite(discountFactor) || discountFactor <= 0d || !double.IsFinite(timeInYears))
        {
            throw new ArgumentException("Curve discount factors must be finite and positive, and year fractions finite.");
        }

        if (timeInYears <= 0d)
        {
            return 0d;
        }

        return -Math.Log(discountFactor) / timeInYears;
    }
}
