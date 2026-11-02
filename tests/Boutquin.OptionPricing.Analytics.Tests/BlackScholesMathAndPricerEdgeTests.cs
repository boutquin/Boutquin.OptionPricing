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
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Analytics.Tests;

/// <summary>
/// Math-seam guard tests for <see cref="BlackScholesMath.ComputeD1D2"/> (F05) and
/// pricer edge-case contract tests (F38 null-Greeks-on-expiry, invalid-enum throw,
/// T=0 intrinsic).
/// </summary>
public sealed class BlackScholesMathAndPricerEdgeTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 15);
    private const Currency Usd = Currency.USD;

    private static OptionMarketData CreateMarket()
    {
        return new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(100m),
            flatVolatility: 0.20,
            discountCurve: new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, Usd, 0.05));
    }

    [Theory]
    [InlineData(0d, 100d, 0.2d, 1d)]   // spot
    [InlineData(100d, 0d, 0.2d, 1d)]   // strike
    [InlineData(100d, 100d, 0d, 1d)]   // volatility
    [InlineData(100d, 100d, 0.2d, 0d)] // time
    [InlineData(-1d, 100d, 0.2d, 1d)]  // negative spot
    public void ComputeD1D2_NonPositiveInput_Throws(double spot, double strike, double vol, double t)
    {
        Action act = () => BlackScholesMath.ComputeD1D2(spot, strike, 0.05d, 0.0d, vol, t);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ComputeD1D2_ValidInput_ReturnsFiniteValues()
    {
        (double d1, double d2) = BlackScholesMath.ComputeD1D2(100d, 100d, 0.05d, 0.0d, 0.20d, 1d);

        double.IsFinite(d1).Should().BeTrue();
        double.IsFinite(d2).Should().BeTrue();
        d2.Should().BeLessThan(d1);
    }

    [Fact]
    public void Price_Expired_HasNullGreeksAndIntrinsicValue()
    {
        var pricer = new BlackScholesPricer();
        var call = new EuropeanOption(CallPut.Call, 90m, Currency.USD, s_valuationDate); // already at expiry

        var result = pricer.Price(call, CreateMarket());

        result.Greeks.Should().BeNull();              // F38: null, consistent with the live path
        result.Price.Should().Be(Currency.USD.Amount(10m));  // intrinsic: max(100 - 90, 0)
        result.Status.Should().Be(CalculationStatus.Success);
    }

    [Fact]
    public void Price_InvalidCallPut_Throws()
    {
        var pricer = new BlackScholesPricer();
        Action act = () => pricer.Price(
            new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6)) with { CallPut = (CallPut)999 },
            CreateMarket());

        act.Should().Throw<Exception>().Where(error => error.GetType() == typeof(ArgumentOutOfRangeException) || error.GetType() == typeof(NotSupportedException));
    }
}
