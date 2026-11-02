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
using Boutquin.Curves.Abstractions.Identifiers;
using Boutquin.Curves.Core.Discounting;
using Boutquin.Domain.Enumerations;
using Boutquin.Domain.Extensions;
using Boutquin.Domain.ValueObjects;
using Boutquin.MarketData.Conventions;
using Boutquin.MarketData.DayCount;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Domain.Tests;

/// <summary>
/// Verifies the construction-time and <c>with</c>-expression invariants of
/// <see cref="OptionMarketData"/> (F04), the cost-of-carry sign of the borrow
/// component of <see cref="OptionMarketData.ForwardPrice(DateOnly)"/> (F03), and
/// the degenerate-discount-factor guard on the implied-rate helpers (F06).
/// </summary>
public sealed class OptionMarketDataValidationTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 15);
    private const Currency Usd = Currency.USD;

    private static OptionMarketData CreateMarket(
        IDiscountCurve? dividendCurve = null,
        IDiscountCurve? borrowCurve = null)
    {
        var disc = new FlatDiscountCurve(new CurveName("USD-DISC"), s_valuationDate, Usd, 0.05);
        return new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(100m),
            flatVolatility: 0.20,
            discountCurve: disc,
            dividendCurve: dividendCurve,
            borrowCurve: borrowCurve);
    }

    // ---- F03: borrow cost reduces the forward (same convention as the pricer's effectiveQ = q + b) ----

    [Fact]
    public void ForwardPrice_WithBorrowCurve_FollowsCarryConventionSharedWithPricer()
    {
        var dividend = new FlatDiscountCurve(new CurveName("USD-DIV"), s_valuationDate, Usd, 0.02);
        var borrow = new FlatDiscountCurve(new CurveName("USD-BORROW"), s_valuationDate, Usd, 0.03);
        var market = CreateMarket(dividend, borrow);
        var expiry = s_valuationDate.AddMonths(9);

        double r = market.RiskFreeRate(expiry);
        double q = market.DividendYield(expiry);
        double b = market.BorrowRate(expiry);
        double t = market.TimeToExpiryInYears(expiry);

        // The pricer prices with effectiveQ = q + b, i.e. forward = S * exp((r - q - b) * T).
        // ForwardPrice must agree with that convention; the old code used (r - q + b).
        double expected = 100d * Math.Exp((r - q - b) * t);

        market.ForwardPrice(expiry).Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void ForwardPrice_WithBorrowCurve_IsBelowTheNoBorrowForward()
    {
        var dividend = new FlatDiscountCurve(new CurveName("USD-DIV"), s_valuationDate, Usd, 0.02);
        var borrow = new FlatDiscountCurve(new CurveName("USD-BORROW"), s_valuationDate, Usd, 0.03);
        var expiry = s_valuationDate.AddMonths(9);

        double fwdNoBorrow = CreateMarket(dividend).ForwardPrice(expiry);
        double fwdWithBorrow = CreateMarket(dividend, borrow).ForwardPrice(expiry);

        fwdWithBorrow.Should().BeLessThan(fwdNoBorrow);
    }

    // ---- F04: init-setter validation cannot be bypassed via a with-expression ----

    [Fact]
    public void With_NonPositiveSpot_Throws()
    {
        var market = CreateMarket();

        Action act = () => _ = market with { Spot = Usd.Amount(-5m) };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void With_ZeroSpot_Throws()
    {
        var market = CreateMarket();

        Action act = () => _ = market with { Spot = Usd.Amount(0m) };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void With_NonPositiveVolatility_Throws()
    {
        var market = CreateMarket();

        Action act = () => _ = market with { FlatVolatility = 0d };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void With_PositiveSpotAndVolatility_Succeeds()
    {
        var market = CreateMarket();

        OptionMarketData updated = market with { Spot = Usd.Amount(123m), FlatVolatility = 0.33d };

        updated.Spot.Should().Be(Usd.Amount(123m));
        updated.FlatVolatility.Should().Be(0.33d);
    }

    // ---- CCY-MD-CTOR-UNSPEC-003: the ctor must reject an Unspecified-currency spot ----

    [Fact]
    public void Constructor_WithUnspecifiedCurrencySpot_Throws()
    {
        var disc = new FlatDiscountCurve(new CurveName("USD-DISC"), s_valuationDate, Usd, 0.05);

        Action act = () => _ = new OptionMarketData(
            s_valuationDate,
            new Money(100m, Currency.Unspecified),
            flatVolatility: 0.20,
            discountCurve: disc);

        act.Should().Throw<ArgumentException>();
    }

    // ---- F06: a curve that returns a non-positive discount factor must not produce NaN/Inf rates ----

    [Fact]
    public void BorrowRate_WhenCurveReturnsNonPositiveDiscountFactor_Throws()
    {
        var disc = new FlatDiscountCurve(new CurveName("USD-DISC"), s_valuationDate, Usd, 0.05);
        var degenerate = new DegenerateDiscountCurve(s_valuationDate, disc.DayCount);
        var market = new OptionMarketData(
            s_valuationDate, Currency.USD.Amount(100m), 0.20, disc, borrowCurve: degenerate);

        Action act = () => market.BorrowRate(s_valuationDate.AddMonths(6));

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>Minimal discount curve that returns a non-positive discount factor, to exercise the F06 guard.</summary>
    private sealed class DegenerateDiscountCurve(DateOnly valuationDate, IYearFractionCalculator dayCount) : IDiscountCurve
    {
        public CurveName Name { get; } = new("DEGENERATE");
        public DateOnly ValuationDate { get; } = valuationDate;
        public Currency Currency => Currency.USD;
        public IYearFractionCalculator DayCount { get; } = dayCount;
        public double ValueAt(DateOnly date) => 0d;
        public double DiscountFactor(DateOnly date) => 0d;
        public double ZeroRate(DateOnly date, CompoundingConvention compounding) => 0d;
        public double InstantaneousForwardRate(DateOnly date) => 0d;
        public IDiscountCurve WithValuationDate(DateOnly newValuationDate) => this;
    }
}
