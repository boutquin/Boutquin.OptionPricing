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
using Boutquin.OptionPricing.Calibration.ImpliedVolatility;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Calibration.Tests;

/// <summary>
/// Tests for the implied-volatility solver's no-arbitrage bounds handling and adaptive
/// bracketing (F09): a target below the discounted-intrinsic floor or above the
/// discounted spot returns a clear failure rather than throwing, and a high implied
/// volatility beyond the default bracket is recovered by adaptive expansion.
/// </summary>
public sealed class ImpliedVolatilitySolverBracketTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 15);
    private static readonly DateOnly s_expiry = new(2026, 7, 15);

    private static OptionMarketData CreateMarket(double vol)
    {
        return new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(100m),
            flatVolatility: vol,
            discountCurve: new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, Currency.USD, 0.05));
    }

    [Fact]
    public void Solve_PriceBelowIntrinsicFloor_FailsGracefully()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiry);
        var market = CreateMarket(0.20);

        // The discounted-intrinsic floor for this ATM call is ≈ 2.47; 1.0 is below it.
        var request = new ImpliedVolatilityRequest(option, market, ObservedPrice: 1.0m, InitialGuess: 0.2);
        var result = new ImpliedVolatilitySolver().Solve(request);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(CalculationStatus.FailedToConverge);
    }

    [Fact]
    public void Solve_PriceAboveDiscountedSpot_FailsGracefully()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiry);
        var market = CreateMarket(0.20);

        // A call can never exceed the (discounted) spot of 100; 105 violates no-arbitrage.
        var request = new ImpliedVolatilityRequest(option, market, ObservedPrice: 105m, InitialGuess: 0.2);
        var result = new ImpliedVolatilitySolver().Solve(request);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(CalculationStatus.FailedToConverge);
    }

    [Fact]
    public void Solve_HighVolatilityBeyondDefaultBracket_IsRecoveredByExpansion()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiry);
        // True vol 6.0 sits above the default upper bracket (5.0): adaptive expansion must find it.
        var priced = CreateMarket(6.0);
        decimal observedPrice = new BlackScholesPricer().Price(option, priced).Price.Amount;

        var request = new ImpliedVolatilityRequest(option, CreateMarket(0.20), observedPrice, InitialGuess: 0.2);
        var result = new ImpliedVolatilitySolver().Solve(request);

        result.IsSuccess.Should().BeTrue();
        result.ImpliedVolatility!.Value.Should().BeApproximately(6.0, 1e-2);
    }
}
