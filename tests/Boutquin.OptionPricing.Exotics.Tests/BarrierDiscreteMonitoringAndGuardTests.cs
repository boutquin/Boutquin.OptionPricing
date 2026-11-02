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
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Exotics.Barriers;
using Boutquin.OptionPricing.Trees.Binomial;
using Boutquin.OptionPricing.Trees.Pricing;
using FluentAssertions;

namespace Boutquin.OptionPricing.Exotics.Tests;

/// <summary>
/// Tests for the discrete-monitoring barrier path (F32 — wires
/// <see cref="BarrierMonitoringAdjustments"/> into <see cref="BarrierTreePricer"/>),
/// argument-null and enum-exhaustiveness guards (F10/F11), and the knock-in-already-breached
/// coverage gap.
/// </summary>
public sealed class BarrierDiscreteMonitoringAndGuardTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 1);
    private static readonly DateOnly s_expiryDate = new(2026, 7, 1);
    private const Currency Usd = Currency.USD;

    private static OptionMarketData CreateMarket(decimal spot, double vol, double r)
    {
        return new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(spot),
            vol,
            new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, Usd, r));
    }

    private static BinomialTree BuildTree(OptionMarketData market, decimal strike, int steps)
    {
        var input = TreeModelInput.FromMarketData(market, strike, s_expiryDate, steps);
        return new CoxRossRubinsteinTreeBuilder().Build(input);
    }

    [Fact]
    public void PriceDiscretelyMonitored_UpAndOutCall_NotCheaperThanContinuous()
    {
        // Discrete monitoring shifts an up barrier UPWARD (H_adj = H*exp(+βσ√(T/m)) > H),
        // making knock-out less likely, so the discretely-monitored price must be >= continuous.
        var market = CreateMarket(100m, 0.20, 0.05);
        var tree = BuildTree(market, 100m, 200);
        var barrierCall = new BarrierOption(CallPut.Call, 100m, Currency.USD, 130m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        var pricer = new BarrierTreePricer();

        decimal continuous = pricer.Price(barrierCall, market, tree).Price.Amount;
        decimal discrete = pricer.PriceDiscretelyMonitored(barrierCall, market, tree, monitoringDates: 12).Price.Amount;

        discrete.Should().BeGreaterThanOrEqualTo(continuous);
    }

    [Fact]
    public void PriceDiscretelyMonitored_InvalidMonitoringDates_Throws()
    {
        var market = CreateMarket(100m, 0.20, 0.05);
        var tree = BuildTree(market, 100m, 50);
        var barrierCall = new BarrierOption(CallPut.Call, 100m, Currency.USD, 130m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);

        new BarrierTreePricer()
            .Invoking(p => p.PriceDiscretelyMonitored(barrierCall, market, tree, monitoringDates: 0))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Price_NullArguments_Throw()
    {
        var market = CreateMarket(100m, 0.20, 0.05);
        var tree = BuildTree(market, 100m, 10);
        var barrier = new BarrierOption(CallPut.Call, 100m, Currency.USD, 130m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        var pricer = new BarrierTreePricer();

        pricer.Invoking(p => p.Price(null!, market, tree)).Should().Throw<ArgumentNullException>();
        pricer.Invoking(p => p.Price(barrier, null!, tree)).Should().Throw<ArgumentNullException>();
        pricer.Invoking(p => p.Price(barrier, market, null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Price_InvalidBarrierDirection_Throws()
    {
        var market = CreateMarket(100m, 0.20, 0.05);
        var tree = BuildTree(market, 100m, 10);
        Action invalid = () => _ = new BarrierOption(CallPut.Call, 100m, Currency.USD, 130m,
            BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate) with
        { Direction = (BarrierDirection)999 };
        invalid.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void KnockIn_AlreadyBreachedAtSpot_ReturnsVanillaValue()
    {
        // Down-and-in put with barrier above spot: already activated at t0, so it prices as vanilla.
        var market = CreateMarket(100m, 0.20, 0.05);
        var tree = BuildTree(market, 100m, 200);

        var vanillaPut = new EuropeanOption(CallPut.Put, 100m, Currency.USD, s_expiryDate);
        decimal vanillaPrice = new EuropeanTreePricer().Price(vanillaPut, market, tree).Price.Amount;

        var knockInPut = new BarrierOption(CallPut.Put, 100m, Currency.USD, 110m, BarrierDirection.Down, BarrierKnockType.In, s_expiryDate);
        var result = new BarrierTreePricer().Price(knockInPut, market, tree);

        result.Diagnostics.Should().ContainKey("barrierBreachedAtSpot");
        result.Price.Amount.Should().BeApproximately(vanillaPrice, 1e-9m);
    }
}
