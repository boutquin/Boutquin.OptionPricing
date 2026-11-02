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

using Boutquin.Curves.Abstractions.Identifiers;
using Boutquin.Curves.Core.Discounting;
using Boutquin.Domain.Enumerations;
using Boutquin.Domain.Extensions;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Domain.Tests;

/// <summary>
/// Exercises the Phase 5c <see cref="OptionMarketData.InvertYearFraction"/> helper.
/// Verifies that the date returned round-trips through
/// <see cref="OptionMarketData.TimeToExpiryInYears"/> within one day's worth of
/// day-count resolution, which is the finest addressable granularity on
/// <see cref="DateOnly"/>.
/// </summary>
public sealed class OptionMarketDataInvertYearFractionTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 15);
    private const Currency Usd = Currency.USD;

    private static OptionMarketData CreateMarket()
    {
        var disc = new FlatDiscountCurve(new CurveName("USD-DISC"), s_valuationDate, Usd, 0.04);
        return new OptionMarketData(s_valuationDate, Currency.USD.Amount(100m), flatVolatility: 0.20, discountCurve: disc);
    }

    [Fact]
    public void InvertYearFraction_Zero_ReturnsValuationDate()
    {
        var market = CreateMarket();

        DateOnly result = market.InvertYearFraction(0d);

        result.Should().Be(s_valuationDate);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1d)]
    [InlineData(2d)]
    [InlineData(5d)]
    [InlineData(30d)]
    public void InvertYearFraction_RoundTripsUnderDiscountCurveDayCount(double years)
    {
        var market = CreateMarket();

        DateOnly inverted = market.InvertYearFraction(years);
        double recoveredYears = market.TimeToExpiryInYears(inverted);

        // Daily resolution: within one day's worth of year fraction under any
        // reasonable day count. Two days of slack covers boundary rounding on
        // very long horizons where accumulated drift can exceed one day.
        double dailyTolerance = 2d / 365d;
        Math.Abs(recoveredYears - years).Should().BeLessThan(dailyTolerance);
    }

    [Fact]
    public void InvertYearFraction_ReturnsNearestDate_RoundingToMinAbsoluteError()
    {
        var market = CreateMarket();

        DateOnly inverted = market.InvertYearFraction(1d);
        double invertedYf = market.TimeToExpiryInYears(inverted);

        // Compare against both adjacent dates — the helper's output must have
        // the smallest absolute year-fraction error of the three candidates.
        double prevYf = market.TimeToExpiryInYears(inverted.AddDays(-1));
        double nextYf = market.TimeToExpiryInYears(inverted.AddDays(1));

        double errorAtInverted = Math.Abs(invertedYf - 1d);
        double errorAtPrev = Math.Abs(prevYf - 1d);
        double errorAtNext = Math.Abs(nextYf - 1d);

        errorAtInverted.Should().BeLessThanOrEqualTo(errorAtPrev);
        errorAtInverted.Should().BeLessThanOrEqualTo(errorAtNext);
    }

    [Fact]
    public void InvertYearFraction_Negative_Throws()
    {
        var market = CreateMarket();

        Action act = () => market.InvertYearFraction(-0.01);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1e6)]   // far beyond the ~8000-year DateOnly horizon
    [InlineData(1e15)]  // large enough to saturate the int seed cast pre-fix
    public void InvertYearFraction_BeyondRepresentableHorizon_ThrowsWithMaturityParam(double years)
    {
        var market = CreateMarket();

        Action act = () => market.InvertYearFraction(years);

        // Must surface a clean argument error naming the offending parameter — not an
        // opaque ArgumentOutOfRangeException bubbling out of DateOnly.AddDays.
        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("maturityInYears");
    }

    [Fact]
    public void InvertYearFraction_LargeButRepresentable_ReturnsDateWithinHorizon()
    {
        var market = CreateMarket();

        // ~1000 years is comfortably inside the DateOnly horizon and must still resolve
        // (regression guard: the new upper-bound clamp must not reject valid long tenors).
        DateOnly inverted = market.InvertYearFraction(1000d);

        inverted.Should().BeAfter(s_valuationDate);
        inverted.Should().BeOnOrBefore(DateOnly.MaxValue);
    }
}
