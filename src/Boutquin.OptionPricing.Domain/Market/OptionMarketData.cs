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
using Boutquin.Curves.Abstractions.Environments;
using Boutquin.Domain.Enumerations;
using Boutquin.Domain.Exceptions;
using Boutquin.Domain.Extensions;
using Boutquin.Domain.ValueObjects;
using Boutquin.MarketData.Abstractions;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Storage.InMemory;

namespace Boutquin.OptionPricing.Domain.Market;

/// <summary>
/// Immutable snapshot of market data required to price an option: spot price,
/// flat volatility, discount curve, and optional dividend, borrow, and projection curves.
/// </summary>
/// <remarks>
/// <para>
/// Curves are provided via Boutquin.Curves <see cref="IDiscountCurve"/> and
/// <see cref="IForwardCurve"/> abstractions. When a dividend or borrow curve is
/// <see langword="null"/>, the corresponding discount factor defaults to 1.0
/// (zero yield).
/// </para>
/// <para>
/// Implied continuous rates are derived from discount factors as −ln(DF) / T.
/// The forward price is computed from the cost-of-carry relationship
/// F = S × DF_div / (DF_disc × DF_borrow), or via the projection curve when supplied.
/// </para>
/// </remarks>
public sealed record OptionMarketData
{
    /// <summary>
    /// Creates a market data snapshot for option pricing.
    /// </summary>
    /// <param name="valuationDate">The as-of date for the pricing calculation.</param>
    /// <param name="spot">
    /// The current underlying asset price as a <see cref="Money"/>. Its amount must be positive;
    /// its currency denominates the market and must match the priced contract's currency.
    /// </param>
    /// <param name="flatVolatility">
    /// Annualized volatility as a decimal fraction (for example 0.20 for 20%). Must be positive.
    /// </param>
    /// <param name="discountCurve">
    /// The risk-free discount curve used to discount future cash flows.
    /// </param>
    /// <param name="dividendCurve">
    /// Optional dividend yield curve. When <see langword="null"/>, dividend yield is zero.
    /// </param>
    /// <param name="borrowCurve">
    /// Optional stock borrow cost curve. When <see langword="null"/>, borrow cost is zero.
    /// </param>
    /// <param name="projectionCurve">
    /// Optional forward projection curve. When supplied, the forward price is computed
    /// from the projected rate rather than from the cost-of-carry relationship.
    /// </param>
    /// <param name="marketLabel">
    /// Optional label identifying the market data source or scenario name.
    /// </param>
    /// <param name="fixingsStore">
    /// Optional historical fixings store for path-dependent pricing models.
    /// When <see langword="null"/>, no fixings are available.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="spot"/> or <paramref name="flatVolatility"/> is zero
    /// or negative.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="spot"/>'s currency is <see cref="Currency.Unspecified"/> — the
    /// spot currency denominates the market and must be a real currency (so a later
    /// <c>with</c>-expression re-set cannot change it away from the <see cref="Currency.Unspecified"/>
    /// sentinel).
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="discountCurve"/> is <see langword="null"/>.
    /// </exception>
    public OptionMarketData(
        DateOnly valuationDate,
        Money spot,
        double flatVolatility,
        IDiscountCurve discountCurve,
        IDiscountCurve? dividendCurve = null,
        IDiscountCurve? borrowCurve = null,
        IForwardCurve? projectionCurve = null,
        string? marketLabel = null,
        IFixingsStore? fixingsStore = null)
    {
        if (spot.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(spot), "Spot must be positive.");
        }

        if (spot.Currency == Currency.Unspecified)
        {
            throw new ArgumentException(
                "Spot currency must not be Currency.Unspecified; it denominates the market and " +
                "must match the priced contract's currency.",
                nameof(spot));
        }

        if (!double.IsFinite(flatVolatility) || flatVolatility <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(flatVolatility), "Flat volatility must be positive and finite.");
        }

        ValuationDate = valuationDate;
        Spot = spot;
        FlatVolatility = flatVolatility;
        DiscountCurve = discountCurve ?? throw new ArgumentNullException(nameof(discountCurve));
        DividendCurve = dividendCurve;
        BorrowCurve = borrowCurve;
        ProjectionCurve = projectionCurve;
        MarketLabel = marketLabel;
        FixingsStore = fixingsStore;
    }

    /// <summary>
    /// The as-of date for this market data snapshot, used as the reference point
    /// for time-to-expiry calculations and curve lookups.
    /// </summary>
    public DateOnly ValuationDate { get; init; }

    /// <summary>
    /// The current underlying asset price as a <see cref="Money"/>, in the same currency as the
    /// priced contract's strike. The amount must be positive; the validation is enforced on
    /// <c>init</c> so it cannot be bypassed via a <c>with</c> expression. Re-setting the spot via a
    /// <c>with</c> expression must preserve the existing currency.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown on <c>init</c> when the spot amount is zero or negative.
    /// </exception>
    /// <exception cref="CurrencyMismatchException">
    /// Thrown on <c>init</c> when the new spot currency differs from the existing spot currency
    /// (a <c>with</c>-expression re-set may not change the market currency).
    /// </exception>
    public Money Spot
    {
        get => _spot;
        init
        {
            if (value.Amount <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Spot must be positive.");
            }

            // The default(Money) currency on a freshly-initialized backing field is
            // Currency.Unspecified; the ctor's first assignment establishes the market currency.
            // A subsequent with-expression re-set must keep that currency.
            if (_spot.Currency != Currency.Unspecified && value.Currency != _spot.Currency)
            {
                throw new CurrencyMismatchException(_spot.Currency, value.Currency);
            }

            _spot = value;
        }
    }

    /// <summary>
    /// Annualized Black-Scholes volatility as a decimal fraction (for example 0.20 for 20%).
    /// Must be positive; the validation is enforced on <c>init</c> so it cannot be
    /// bypassed via a <c>with</c> expression.
    /// </summary>
    public double FlatVolatility
    {
        get => _flatVolatility;
        init
        {
            if (!double.IsFinite(value) || value <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Flat volatility must be positive and finite.");
            }

            _flatVolatility = value;
        }
    }

    private readonly Money _spot;
    private readonly double _flatVolatility;

    /// <summary>
    /// The risk-free discount curve used to compute present values and implied rates.
    /// </summary>
    public IDiscountCurve DiscountCurve
    {
        get => _discountCurve;
        init => _discountCurve = value ?? throw new ArgumentNullException(nameof(value));
    }

    private readonly IDiscountCurve _discountCurve = null!;

    /// <summary>
    /// Optional dividend yield discount curve. When <see langword="null"/>, dividend
    /// discount factors default to 1.0 (zero continuous dividend yield).
    /// </summary>
    public IDiscountCurve? DividendCurve { get; init; }

    /// <summary>
    /// Optional stock borrow cost discount curve. When <see langword="null"/>, borrow
    /// discount factors default to 1.0 (zero borrow cost).
    /// </summary>
    public IDiscountCurve? BorrowCurve { get; init; }

    /// <summary>
    /// Optional forward rate projection curve. When supplied, the forward price is
    /// computed from the projected rate rather than from the cost-of-carry relationship.
    /// </summary>
    public IForwardCurve? ProjectionCurve { get; init; }

    /// <summary>
    /// Optional label identifying the market data source or scenario name for
    /// diagnostic and audit purposes.
    /// </summary>
    public string? MarketLabel { get; init; }

    /// <summary>
    /// Optional historical fixings store for path-dependent pricing models
    /// (e.g., Asian options, lookbacks) that require past benchmark rates.
    /// </summary>
    public IFixingsStore? FixingsStore { get; init; }

    /// <summary>
    /// Creates an <see cref="OptionMarketData"/> by resolving curves from an
    /// <see cref="ICurveGroup"/> by <see cref="CurveRole"/> and currency.
    /// </summary>
    /// <param name="curveGroup">The curve group containing discount, dividend, borrow, and/or forward curves.</param>
    /// <param name="currency">The currency used to look up curves via <see cref="CurveReference"/>.</param>
    /// <param name="spot">Current underlying asset price. Must be positive.</param>
    /// <param name="flatVolatility">Annualized volatility as a decimal fraction. Must be positive.</param>
    /// <param name="benchmark">Optional benchmark name for recipe-built curve groups where curves carry a benchmark identity.</param>
    /// <param name="marketLabel">Optional label for diagnostic purposes.</param>
    /// <returns>A new <see cref="OptionMarketData"/> with curves resolved from the group.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the curve group does not contain a discount curve for the given currency.
    /// </exception>
    public static OptionMarketData FromCurveGroup(
        ICurveGroup curveGroup,
        Currency currency,
        decimal spot,
        double flatVolatility,
        BenchmarkName? benchmark = null,
        string? marketLabel = null)
    {
        var discount = FindCurveByRoleAndCurrency<IDiscountCurve>(curveGroup, CurveRole.Discount, currency, benchmark)
            ?? throw new InvalidOperationException($"Curve group '{curveGroup.Name}' does not contain a discount curve for {currency}.");

        var dividend = FindCurveByRoleAndCurrency<IDiscountCurve>(curveGroup, CurveRole.Dividend, currency);
        var borrow = FindCurveByRoleAndCurrency<IDiscountCurve>(curveGroup, CurveRole.Borrow, currency);
        var forward = FindCurveByRoleAndCurrency<IForwardCurve>(curveGroup, CurveRole.Forward, currency, benchmark);

        return new OptionMarketData(
            curveGroup.ValuationDate,
            currency.Amount(spot),
            flatVolatility,
            discount,
            dividend,
            borrow,
            forward,
            marketLabel);
    }

    /// <summary>
    /// Creates an <see cref="OptionMarketData"/> from a <see cref="MarketEnvironment"/>,
    /// resolving curves by role and currency, and mapping environment fixings into
    /// an <see cref="IFixingsStore"/>.
    /// </summary>
    /// <param name="env">The market environment containing curves, benchmarks, and fixings.</param>
    /// <param name="currency">The currency used to look up curves via <see cref="CurveReference"/>.</param>
    /// <param name="spot">Current underlying asset price. Must be positive.</param>
    /// <param name="flatVolatility">Annualized volatility as a decimal fraction. Must be positive.</param>
    /// <param name="marketLabel">Optional label for diagnostic purposes.</param>
    /// <returns>A new <see cref="OptionMarketData"/> with curves and fixings from the environment.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the environment does not contain a discount curve for the given currency.
    /// </exception>
    public static OptionMarketData FromMarketEnvironment(
        MarketEnvironment env,
        Currency currency,
        decimal spot,
        double flatVolatility,
        string? marketLabel = null)
    {
        var discountRef = new CurveReference(CurveRole.Discount, currency);
        if (!env.Curves.TryGetValue(discountRef, out ICurve? discountCurve) || discountCurve is not IDiscountCurve discount)
        {
            throw new InvalidOperationException($"Market environment does not contain a discount curve for {currency}.");
        }

        var dividendRef = new CurveReference(CurveRole.Dividend, currency);
        env.Curves.TryGetValue(dividendRef, out ICurve? dividendRaw);
        var dividend = dividendRaw as IDiscountCurve;

        var borrowRef = new CurveReference(CurveRole.Borrow, currency);
        env.Curves.TryGetValue(borrowRef, out ICurve? borrowRaw);
        var borrow = borrowRaw as IDiscountCurve;

        var forwardRef = new CurveReference(CurveRole.Forward, currency);
        env.Curves.TryGetValue(forwardRef, out ICurve? forwardRaw);
        var forward = forwardRaw as IForwardCurve;

        InMemoryFixingsStore? fixingsStore = null;
        if (env.Fixings.Count > 0)
        {
            fixingsStore = new InMemoryFixingsStore(
                env.Fixings.Select(fixing => (new BenchmarkName(fixing.Key), env.ValuationDate, fixing.Value)));
        }

        return new OptionMarketData(
            env.ValuationDate,
            currency.Amount(spot),
            flatVolatility,
            discount,
            dividend,
            borrow,
            forward,
            marketLabel,
            fixingsStore);
    }

    /// <summary>
    /// Returns a copy of this market data with a different flat volatility, useful
    /// for implied volatility search iterations.
    /// </summary>
    /// <param name="flatVolatility">
    /// The replacement annualized volatility as a decimal fraction. Must be positive.
    /// </param>
    /// <returns>A new <see cref="OptionMarketData"/> with the updated volatility.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="flatVolatility"/> is zero or negative.
    /// </exception>
    public OptionMarketData WithFlatVolatility(double flatVolatility)
    {
        if (!double.IsFinite(flatVolatility) || flatVolatility <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(flatVolatility), "Flat volatility must be positive and finite.");
        }

        return this with { FlatVolatility = flatVolatility };
    }

    /// <summary>
    /// Returns a copy of this market data rolled forward to <paramref name="stepDate"/>
    /// with <paramref name="newSpot"/>, propagating the roll into every contained curve
    /// via <see cref="IDiscountCurve.WithValuationDate(DateOnly)"/> so discount factors,
    /// implied rates, and forward rates all reflect the new valuation date.
    /// </summary>
    /// <param name="stepDate">
    /// The new valuation date. Must be at or after the original <see cref="ValuationDate"/>.
    /// </param>
    /// <param name="newSpot">
    /// The realized spot at <paramref name="stepDate"/> as a <see cref="Money"/>. Its amount must
    /// be positive and its currency must match the existing <see cref="Spot"/> currency.
    /// </param>
    /// <returns>
    /// A new <see cref="OptionMarketData"/> with <paramref name="stepDate"/> as
    /// <see cref="ValuationDate"/>, <paramref name="newSpot"/> as <see cref="Spot"/>,
    /// and every contained discount and projection curve rolled forward.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the <paramref name="newSpot"/> amount is zero or negative, or
    /// <paramref name="stepDate"/> precedes <see cref="ValuationDate"/>.
    /// </exception>
    /// <exception cref="CurrencyMismatchException">
    /// Thrown when the <paramref name="newSpot"/> currency differs from the existing
    /// <see cref="Spot"/> currency.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Plain record cloning via <c>market with { ValuationDate = stepDate, ... }</c> is
    /// <b>not</b> equivalent: the contained curves keep their own valuation dates, so
    /// <c>DiscountFactor(expiry)</c> continues to return <c>exp(-r · T_total)</c> while
    /// <c>TimeToExpiryInYears</c> returns <c>T_remaining</c>, silently producing a
    /// time-stretched implied rate <c>r · T_total / T_remaining</c>. Always use
    /// <see cref="RolledTo"/> to advance a market snapshot in time.
    /// </para>
    /// <para>
    /// Concrete curve roll semantics are defined by the curve's own
    /// <see cref="IDiscountCurve.WithValuationDate(DateOnly)"/> implementation. Curves
    /// that do not support rolling will throw <see cref="NotSupportedException"/>.
    /// </para>
    /// </remarks>
    public OptionMarketData RolledTo(DateOnly stepDate, Money newSpot)
    {
        if (newSpot.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(newSpot), "Spot must be positive.");
        }

        if (newSpot.Currency != _spot.Currency)
        {
            throw new CurrencyMismatchException(_spot.Currency, newSpot.Currency);
        }

        if (stepDate < ValuationDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stepDate),
                "Step date cannot precede the market's current valuation date.");
        }

        return new OptionMarketData(
            stepDate,
            newSpot,
            FlatVolatility,
            DiscountCurve.WithValuationDate(stepDate),
            DividendCurve?.WithValuationDate(stepDate),
            BorrowCurve?.WithValuationDate(stepDate),
            ProjectionCurve?.WithValuationDate(stepDate),
            MarketLabel,
            FixingsStore);
    }

    /// <summary>
    /// Computes the time from the valuation date to the specified expiry date in
    /// year fractions, using the day-count convention of the discount curve.
    /// </summary>
    /// <param name="expiryDate">The option expiry date.</param>
    /// <returns>
    /// The year fraction to expiry, or 0.0 when the expiry date is on or before
    /// the valuation date.
    /// </returns>
    public double TimeToExpiryInYears(DateOnly expiryDate)
    {
        if (expiryDate <= ValuationDate)
        {
            return 0d;
        }

        return DiscountCurve.DayCount.YearFraction(ValuationDate, expiryDate);
    }

    /// <summary>
    /// Inverts <see cref="TimeToExpiryInYears"/>: given a target year fraction,
    /// returns the <see cref="DateOnly"/> whose year fraction from
    /// <see cref="ValuationDate"/> under the discount curve's day-count convention
    /// is closest to <paramref name="maturityInYears"/>, rounded to the nearest day.
    /// </summary>
    /// <param name="maturityInYears">
    /// Target year fraction from the valuation date. Must be non-negative.
    /// </param>
    /// <returns>
    /// A date such that <see cref="TimeToExpiryInYears"/> at that date approximates
    /// <paramref name="maturityInYears"/> under the discount curve's day-count convention.
    /// When <paramref name="maturityInYears"/> is zero, returns <see cref="ValuationDate"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="maturityInYears"/> is negative, or when it exceeds the
    /// representable date horizon (the year fraction from <see cref="ValuationDate"/> to
    /// <see cref="DateOnly.MaxValue"/> under the discount curve's day count).
    /// </exception>
    /// <remarks>
    /// <para>
    /// This is Phase 5c's replacement for ad-hoc <c>(int)(maturityYears * 365.25)</c>
    /// conversions that bypass the curve's <c>IYearFractionCalculator</c>. The inversion
    /// uses bisection over calendar days: because every
    /// <see cref="Boutquin.MarketData.DayCount.IYearFractionCalculator"/> implementation
    /// is monotone non-decreasing in the end date, bisection converges on the closest
    /// representable date in O(log Δdays) day-count evaluations.
    /// </para>
    /// <para>
    /// Rounding is to the nearest day — vol surfaces and implied trees operate on daily
    /// granularity, and calendar dates are the coarsest representable unit in
    /// <see cref="DateOnly"/>. When two adjacent dates straddle the target, the one with
    /// the smaller absolute year-fraction error wins; ties break to the earlier date.
    /// </para>
    /// </remarks>
    public DateOnly InvertYearFraction(double maturityInYears)
    {
        if (!double.IsFinite(maturityInYears) || maturityInYears < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maturityInYears),
                "Maturity year fraction must be finite and non-negative.");
        }

        if (maturityInYears == 0d)
        {
            return ValuationDate;
        }

        var dayCount = DiscountCurve.DayCount;

        // Upper-bound guard: the result must land on a representable DateOnly. Reject any
        // maturity beyond the curve's horizon (ValuationDate → DateOnly.MaxValue) up front,
        // rather than letting the 366-day seed below overflow the int cast or throw an
        // opaque ArgumentOutOfRangeException from DateOnly.AddDays. With this guard in place
        // maturityInYears is bounded by ~(maxDays / 365), so the seed cast cannot saturate.
        double maxYearFraction = dayCount.YearFraction(ValuationDate, DateOnly.MaxValue);
        if (maturityInYears > maxYearFraction)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maturityInYears),
                maturityInYears,
                $"Maturity year fraction exceeds the representable date horizon " +
                $"({maxYearFraction:R} years to {DateOnly.MaxValue:O}).");
        }

        int maxDays = DateOnly.MaxValue.DayNumber - ValuationDate.DayNumber;

        // Seed the upper bound using 366-day years, guaranteeing the target year fraction
        // is bracketed for every sub-daily or sub-annual day-count convention in use.
        // Clamp to maxDays so the bracket date never exceeds DateOnly.MaxValue.
        int upperDays = Math.Min(maxDays, Math.Max(1, (int)Math.Ceiling((maturityInYears + 1d) * 366d)));
        DateOnly upper = ValuationDate.AddDays(upperDays);

        // Expand the upper bound defensively in the unlikely event the seed undershoots
        // (e.g., a convention that deflates year fractions below 1/366 per calendar day),
        // never past maxDays — which brackets the target per the horizon guard above.
        while (dayCount.YearFraction(ValuationDate, upper) < maturityInYears && upperDays < maxDays)
        {
            upperDays = Math.Min(maxDays, upperDays * 2);
            upper = ValuationDate.AddDays(upperDays);
        }

        int lo = 0;
        int hi = upperDays;
        while (hi - lo > 1)
        {
            int mid = lo + ((hi - lo) / 2);
            double midYf = dayCount.YearFraction(ValuationDate, ValuationDate.AddDays(mid));
            if (midYf < maturityInYears)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        double loYf = dayCount.YearFraction(ValuationDate, ValuationDate.AddDays(lo));
        double hiYf = dayCount.YearFraction(ValuationDate, ValuationDate.AddDays(hi));
        int bestDays = Math.Abs(hiYf - maturityInYears) < Math.Abs(loYf - maturityInYears) ? hi : lo;
        return ValuationDate.AddDays(bestDays);
    }

    /// <summary>
    /// Returns the risk-free discount factor from the discount curve for the given date.
    /// </summary>
    /// <param name="date">The future date to discount to the valuation date.</param>
    /// <returns>The discount factor DF such that PV = FV × DF.</returns>
    public double DiscountFactor(DateOnly date)
    {
        return DiscountCurve.DiscountFactor(date);
    }

    /// <summary>
    /// Returns the dividend discount factor for the given date, or 1.0 when no
    /// dividend curve is supplied (implying zero dividend yield).
    /// </summary>
    /// <param name="date">The future date.</param>
    /// <returns>The dividend discount factor.</returns>
    public double DividendDiscountFactor(DateOnly date)
    {
        return DividendCurve?.DiscountFactor(date) ?? 1d;
    }

    /// <summary>
    /// Returns the borrow cost discount factor for the given date, or 1.0 when no
    /// borrow curve is supplied (implying zero borrow cost).
    /// </summary>
    /// <param name="date">The future date.</param>
    /// <returns>The borrow discount factor.</returns>
    public double BorrowDiscountFactor(DateOnly date)
    {
        return BorrowCurve?.DiscountFactor(date) ?? 1d;
    }

    /// <summary>
    /// Derives the continuously compounded risk-free rate to the given expiry date
    /// from the discount curve: r = −ln(DF) / T.
    /// </summary>
    /// <param name="expiryDate">The option expiry date.</param>
    /// <returns>
    /// The implied continuous risk-free rate, or 0.0 when the expiry is on or before
    /// the valuation date.
    /// </returns>
    public double RiskFreeRate(DateOnly expiryDate)
    {
        return ImpliedContinuousRate(DiscountFactor(expiryDate), TimeToExpiryInYears(expiryDate));
    }

    /// <summary>
    /// Derives the continuously compounded dividend yield to the given expiry date
    /// from the dividend curve: q = −ln(DF_div) / T.
    /// </summary>
    /// <param name="expiryDate">The option expiry date.</param>
    /// <returns>
    /// The implied continuous dividend yield, or 0.0 when the expiry is on or before
    /// the valuation date or no dividend curve is supplied.
    /// </returns>
    public double DividendYield(DateOnly expiryDate)
    {
        return ImpliedContinuousRate(DividendDiscountFactor(expiryDate), TimeToExpiryInYears(expiryDate));
    }

    /// <summary>
    /// Derives the continuously compounded borrow rate to the given expiry date
    /// from the borrow curve: b = −ln(DF_borrow) / T.
    /// </summary>
    /// <param name="expiryDate">The option expiry date.</param>
    /// <returns>
    /// The implied continuous borrow rate, or 0.0 when the expiry is on or before
    /// the valuation date or no borrow curve is supplied.
    /// </returns>
    public double BorrowRate(DateOnly expiryDate)
    {
        return ImpliedContinuousRate(BorrowDiscountFactor(expiryDate), TimeToExpiryInYears(expiryDate));
    }

    /// <summary>
    /// Computes the forward price of the underlying asset to the given expiry date.
    /// </summary>
    /// <param name="expiryDate">The option expiry date.</param>
    /// <returns>
    /// The forward price. When the expiry is on or before the valuation date, returns
    /// the current spot price. When a projection curve is available, uses
    /// F = S × exp(projectedRate × T); otherwise uses the cost-of-carry formula
    /// F = S × DF_div × DF_borrow / DF_disc = S × exp((r − q − b) × T), where the
    /// borrow cost b reduces the forward (consistent with the pricer's effectiveQ = q + b).
    /// </returns>
    public double ForwardPrice(DateOnly expiryDate)
    {
        if (expiryDate <= ValuationDate)
        {
            return (double)Spot.Amount;
        }

        if (ProjectionCurve is not null)
        {
            double projectedRate = ProjectionCurve.ForwardRate(ValuationDate, expiryDate);
            double timeInYears = TimeToExpiryInYears(expiryDate);
            return (double)Spot.Amount * Math.Exp(projectedRate * timeInYears);
        }

        double discountFactor = DiscountFactor(expiryDate);
        double dividendDiscountFactor = DividendDiscountFactor(expiryDate);
        double borrowDiscountFactor = BorrowDiscountFactor(expiryDate);

        // Borrow cost behaves like an additional yield that reduces the forward,
        // consistent with the pricer's effectiveQ = q + b (see BlackScholesPricer).
        // Hence the borrow discount factor multiplies (it does not divide) the spot:
        //   F = S * DF_div * DF_borrow / DF_disc = S * exp((r - q - b) * T).
        return (double)Spot.Amount * dividendDiscountFactor * borrowDiscountFactor / discountFactor;
    }

    private static double ImpliedContinuousRate(double discountFactor, double timeInYears)
    {
        if (timeInYears <= 0d)
        {
            return 0d;
        }

        if (!double.IsFinite(discountFactor) || discountFactor <= 0d)
        {
            throw new InvalidOperationException(
                $"Curve produced a non-positive or non-finite discount factor ({discountFactor}); cannot derive a continuous rate.");
        }

        return -Math.Log(discountFactor) / timeInYears;
    }

    /// <summary>
    /// Resolves a curve by an exact <see cref="CurveReference"/> key (role, currency, and the
    /// supplied <paramref name="benchmark"/>), returning <c>null</c> when no curve matches that
    /// key or the matched curve is not a <typeparamref name="TCurve"/>.
    /// </summary>
    /// <remarks>
    /// The lookup is a single exact-key match — <see cref="ICurveGroup"/> exposes no way to
    /// enumerate curves by role and currency independent of benchmark, so there is no
    /// benchmark-agnostic fallback. Callers using a recipe-built group whose curves carry a
    /// benchmark identity must pass the matching <paramref name="benchmark"/>; passing
    /// <c>null</c> resolves only curves stored under the null-benchmark key (the manual-build
    /// convention). Note that <see cref="FromCurveGroup"/> looks up the dividend and borrow
    /// curves with a null benchmark, so those resolve only when stored under the null key.
    /// </remarks>
    private static TCurve? FindCurveByRoleAndCurrency<TCurve>(
        ICurveGroup curveGroup, CurveRole role, Currency currency, BenchmarkName? benchmark = null)
        where TCurve : class, ICurve
    {
        var reference = new CurveReference(role, currency, benchmark);
        if (curveGroup.TryGetCurve(reference, out ICurve? curve) && curve is TCurve match)
        {
            return match;
        }

        return null;
    }
}
