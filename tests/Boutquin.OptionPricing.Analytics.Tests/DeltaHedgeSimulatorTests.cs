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
using Boutquin.OptionPricing.Analytics.Hedging;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Analytics.Tests;

/// <summary>
/// Direct unit tests for <see cref="DeltaHedgeSimulator"/>. The simulator's discrete
/// delta-hedge math was validated against an external Python reference during the
/// 2026-06-20 review; these tests pin the contract so future refactors are caught by CI:
/// the degenerate single-spot path, one delta recorded per rebalance, the inception
/// delta/price matching the standalone pricer and Greek provider, and near-perfect
/// replication of a deep-in-the-money (forward-like) call on a flat path.
/// </summary>
public sealed class DeltaHedgeSimulatorTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 15);
    private const Currency Usd = Currency.USD;

    private static OptionMarketData CreateMarket(double r = 0.05, double q = 0.0)
        => new(
            s_valuationDate,
            spot: Currency.USD.Amount(100m),
            flatVolatility: 0.20,
            discountCurve: new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, Usd, r),
            dividendCurve: new FlatDiscountCurve(new CurveName("DIV"), s_valuationDate, Usd, q));

    [Fact]
    public void Simulate_DegenerateSingleSpotPath_ReturnsEmptyZeroResult()
    {
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var spots = new[] { 100m }; // zero steps (spots.Count - 1 < 1)

        var result = new DeltaHedgeSimulator()
            .Simulate(new DeltaHedgeSimulationRequest(call, CreateMarket(), spots));

        result.Deltas.Should().BeEmpty();
        result.StartingOptionPrice.Should().Be(0m);
        result.OptionPayoff.Should().Be(0m);
        result.FinalPortfolioValue.Should().Be(0m);
        result.HedgingError.Should().Be(0m);
        result.Spots.Should().Equal(spots);

        // The degenerate sentinel is NOT a successful simulation — the ICalculationResult
        // contract must let callers distinguish it from a genuine zero-payoff hedge.
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Simulate_SingleStep_RecordsInceptionDeltaAndPriceFromStandaloneModels()
    {
        var market = CreateMarket();
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var spots = new[] { 100m, 110m };

        var result = new DeltaHedgeSimulator()
            .Simulate(new DeltaHedgeSimulationRequest(call, market, spots));

        double expectedDelta = new BlackScholesGreeks().CalculateGreeks(call, market).Delta!.Value;
        decimal expectedPrice = new BlackScholesPricer().Price(call, market).Price.Amount;

        result.IsSuccess.Should().BeTrue(); // a real run with at least one rebalance
        result.Deltas.Should().ContainSingle();
        result.Deltas[0].Should().BeApproximately(expectedDelta, 1e-12);
        result.StartingOptionPrice.Should().Be(expectedPrice);
        result.OptionPayoff.Should().Be(10m); // max(110 - 100, 0)

        // The reported error must be exactly the portfolio-minus-payoff identity.
        result.HedgingError.Should().Be(result.FinalPortfolioValue - result.OptionPayoff);
    }

    [Fact]
    public void Simulate_RecordsOneDeltaPerRebalanceStep()
    {
        var market = CreateMarket();
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var spots = Enumerable.Repeat(100m, 11).ToArray(); // 10 rebalance steps

        var result = new DeltaHedgeSimulator()
            .Simulate(new DeltaHedgeSimulationRequest(call, market, spots));

        result.Deltas.Should().HaveCount(spots.Length - 1);
        result.Deltas.Should().OnlyContain(d => d >= 0d && d <= 1d); // call delta is in [0, 1]
    }

    [Fact]
    public void Simulate_DeepInTheMoneyCallOnFlatPath_ReplicatesNearlyPerfectly()
    {
        // A deep-ITM call has delta ≈ 1 throughout, so a (near-)static one-share hedge
        // replicates its forward-like payoff almost exactly. On a flat spot path the
        // discrete-rebalancing error collapses to ~0, which exercises the self-financing
        // cash-growth and share-rebalance accounting end to end.
        var market = CreateMarket();
        var call = new EuropeanOption(CallPut.Call, 1m, Currency.USD, s_valuationDate.AddMonths(6));
        var spots = Enumerable.Repeat(100m, 51).ToArray(); // 50 flat steps

        var result = new DeltaHedgeSimulator()
            .Simulate(new DeltaHedgeSimulationRequest(call, market, spots));

        result.OptionPayoff.Should().Be(99m); // max(100 - 1, 0)
        Math.Abs(result.HedgingError).Should().BeLessThan(0.5m);
    }
}
