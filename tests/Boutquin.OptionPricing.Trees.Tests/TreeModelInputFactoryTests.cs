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
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.Curves.Core.Discounting;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Trees.Binomial;
using FluentAssertions;

namespace Boutquin.OptionPricing.Trees.Tests;

public sealed class TreeModelInputFactoryTests
{
    private static readonly DateOnly s_valuationDate = new(2025, 6, 15);
    private static readonly DateOnly s_expiryDate = new(2025, 12, 15);
    private const CurrencyCode s_usd = CurrencyCode.USD;

    private static OptionMarketData CreateMarket(
        double riskFreeRate = 0.05,
        double dividendYield = 0.02,
        double borrowRate = 0.01)
    {
        var discountCurve = new FlatDiscountCurve(new CurveName("USD-DISC"), s_valuationDate, s_usd, riskFreeRate);
        var dividendCurve = new FlatDiscountCurve(new CurveName("DIVIDEND"), s_valuationDate, s_usd, dividendYield);
        var borrowCurve = new FlatDiscountCurve(new CurveName("BORROW"), s_valuationDate, s_usd, borrowRate);
        return new OptionMarketData(s_valuationDate, 100m, 0.20, discountCurve, dividendCurve, borrowCurve);
    }

    [Fact]
    public void FromMarketData_ShouldCopyAllCurves()
    {
        var market = CreateMarket();

        var input = TreeModelInput.FromMarketData(market, 100m, s_expiryDate, 200);

        input.DiscountCurve.Should().BeSameAs(market.DiscountCurve);
        input.DividendCurve.Should().BeSameAs(market.DividendCurve);
        input.BorrowCurve.Should().BeSameAs(market.BorrowCurve);
        input.Spot.Should().Be(market.Spot);
        input.Volatility.Should().Be(market.FlatVolatility);
        input.ValuationDate.Should().Be(market.ValuationDate);
        input.ExpiryDate.Should().Be(s_expiryDate);
        input.Steps.Should().Be(200);
        input.Strike.Should().Be(100m);
    }

    [Fact]
    public void FromMarketData_ShouldComputeIdenticalRates()
    {
        var market = CreateMarket();
        var fromFactory = TreeModelInput.FromMarketData(market, 100m, s_expiryDate, 200);
        var manual = new TreeModelInput(market.Spot, 100m, s_valuationDate, s_expiryDate,
            market.DiscountCurve, market.FlatVolatility, 200, market.DividendCurve, market.BorrowCurve);

        fromFactory.RiskFreeRate().Should().BeApproximately(manual.RiskFreeRate(), 1e-12);
        fromFactory.DividendYield().Should().BeApproximately(manual.DividendYield(), 1e-12);
        fromFactory.BorrowRate().Should().BeApproximately(manual.BorrowRate(), 1e-12);
        fromFactory.TimeToExpiryInYears().Should().BeApproximately(manual.TimeToExpiryInYears(), 1e-12);
    }

    [Fact]
    public void FromMarketData_TimeToExpiry_ShouldUseCurveDayCount()
    {
        var market = CreateMarket();
        var input = TreeModelInput.FromMarketData(market, 100m, s_expiryDate, 200);

        // The year fraction should come from the curve's DayCount, not from a hardcoded 365.25
        double expected = market.DiscountCurve.DayCount.YearFraction(s_valuationDate, s_expiryDate);
        input.TimeToExpiryInYears().Should().BeApproximately(expected, 1e-12);
    }

    [Fact]
    public void FromMarketData_ShouldHandleNullOptionalCurves()
    {
        var discountCurve = new FlatDiscountCurve(new CurveName("USD-DISC"), s_valuationDate, s_usd, 0.05);
        var market = new OptionMarketData(s_valuationDate, 100m, 0.20, discountCurve);

        var input = TreeModelInput.FromMarketData(market, 100m, s_expiryDate, 200);

        input.DividendCurve.Should().BeNull();
        input.BorrowCurve.Should().BeNull();
        input.DividendYield().Should().Be(0d);
        input.BorrowRate().Should().Be(0d);
    }
}
