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
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Calibration.ImpliedVolatility;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Calibration.Tests;

public sealed class ImpliedVolatilitySolverTests
{
    [Fact]
    public void Solve_ShouldRecoverFlatVolatility()
    {
        DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
        var option = new EuropeanOption(CallPut.Call, 100m, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);
        decimal observedPrice = new BlackScholesPricer().Price(option, market).Price;

        var request = new ImpliedVolatilityRequest(option, market.WithFlatVolatility(0.10d), observedPrice, 0.20d);
        var result = new ImpliedVolatilitySolver().Solve(request);

        result.IsSuccess.Should().BeTrue();
        result.ImpliedVolatility.Should().NotBeNull();
        result.ImpliedVolatility!.Value.Should().BeApproximately(0.20d, 1e-4d);
    }

    [Fact]
    public void Solve_Put_ShouldRecoverFlatVolatility()
    {
        DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
        var option = new EuropeanOption(CallPut.Put, 100m, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.25d, 0.03d, 0.01d);
        decimal observedPrice = new BlackScholesPricer().Price(option, market).Price;

        var request = new ImpliedVolatilityRequest(option, market.WithFlatVolatility(0.15d), observedPrice, 0.15d);
        var result = new ImpliedVolatilitySolver().Solve(request);

        result.IsSuccess.Should().BeTrue();
        result.ImpliedVolatility.Should().NotBeNull();
        result.ImpliedVolatility!.Value.Should().BeApproximately(0.25d, 1e-4d);
    }

    [Fact]
    public void Solve_DeepInTheMoney_ShouldConverge()
    {
        DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
        var option = new EuropeanOption(CallPut.Call, 100m, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 150m, 0.30d, 0.03d, 0.01d);
        decimal observedPrice = new BlackScholesPricer().Price(option, market).Price;

        var request = new ImpliedVolatilityRequest(option, market.WithFlatVolatility(0.15d), observedPrice, 0.20d);
        var result = new ImpliedVolatilitySolver().Solve(request);

        result.IsSuccess.Should().BeTrue();
        result.ImpliedVolatility!.Value.Should().BeApproximately(0.30d, 1e-4d);
    }

    [Fact]
    public void Solve_DeepOutOfTheMoney_ShouldConverge()
    {
        DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
        var option = new EuropeanOption(CallPut.Call, 100m, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 50m, 0.30d, 0.03d, 0.01d);
        decimal observedPrice = new BlackScholesPricer().Price(option, market).Price;

        var request = new ImpliedVolatilityRequest(option, market.WithFlatVolatility(0.15d), observedPrice, 0.20d);
        var result = new ImpliedVolatilitySolver().Solve(request);

        result.IsSuccess.Should().BeTrue();
        result.ImpliedVolatility!.Value.Should().BeApproximately(0.30d, 1e-4d);
    }

    [Fact]
    public void Solve_AmericanOption_ShouldReturnUnsupported()
    {
        DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
        var option = new AmericanOption(CallPut.Put, 100m, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);

        var request = new ImpliedVolatilityRequest(option, market, 5.0m, 0.20d);
        var result = new ImpliedVolatilitySolver().Solve(request);

        result.Status.Should().Be(CalculationStatus.UnsupportedScenario);
        result.ImpliedVolatility.Should().BeNull();
    }

    [Fact]
    public void Solve_ZeroObservedPrice_ShouldNotConverge()
    {
        DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
        var option = new EuropeanOption(CallPut.Call, 100m, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);

        // Price of 0 means the objective is always negative in the bracket — no root
        var request = new ImpliedVolatilityRequest(option, market.WithFlatVolatility(0.15d), 0m, 0.15d);
        var result = new ImpliedVolatilitySolver().Solve(request);

        result.IsSuccess.Should().BeFalse();
    }

    private static OptionMarketData CreateMarket(DateOnly valuationDate, decimal spot, double flatVolatility, double riskFreeRate, double dividendYield)
    {
        return new OptionMarketData(
            valuationDate,
            spot,
            flatVolatility,
            new FlatDiscountCurve(new CurveName("USD-OIS"), valuationDate, CurrencyCode.USD, riskFreeRate),
            new FlatDiscountCurve(new CurveName("DIVIDEND"), valuationDate, CurrencyCode.USD, dividendYield));
    }
}
