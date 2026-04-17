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
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Domain.Results;
using Boutquin.OptionPricing.Exotics.Barriers;
using Boutquin.OptionPricing.Trees.Binomial;
using Boutquin.OptionPricing.Trees.Pricing;
using FluentAssertions;

namespace Boutquin.OptionPricing.Exotics.Tests;

/// <summary>
/// Tests for <see cref="BarrierTreePricer"/> — backward induction with barrier monitoring
/// on a CRR binomial tree.
/// </summary>
public sealed class BarrierTreePricerTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 1);
    private static readonly DateOnly s_expiryDate = new(2026, 7, 1);

    [Fact]
    public void DownAndOutPut_ShouldBeCheaperThanVanillaPut()
    {
        // A down-and-out put can be knocked out, so it should cost less than vanilla
        var market = CreateMarket(100m, 0.20, 0.05);
        var tree = BuildTree(market, 100m, 200);

        var vanillaPut = new EuropeanOption(CallPut.Put, 100m, s_expiryDate);
        decimal vanillaPrice = new EuropeanTreePricer().Price(vanillaPut, market, tree).Price;

        var barrierPut = new BarrierOption(CallPut.Put, 100m, 80m, BarrierDirection.Down, BarrierKnockType.Out, s_expiryDate);
        PriceResult barrierResult = new BarrierTreePricer().Price(barrierPut, market, tree);

        barrierResult.IsSuccess.Should().BeTrue();
        barrierResult.Price.Should().BeLessThan(vanillaPrice);
        barrierResult.Price.Should().BeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public void UpAndOutCall_ShouldBeCheaperThanVanillaCall()
    {
        // An up-and-out call can be knocked out, so it should cost less than vanilla
        var market = CreateMarket(100m, 0.20, 0.05);
        var tree = BuildTree(market, 100m, 200);

        var vanillaCall = new EuropeanOption(CallPut.Call, 100m, s_expiryDate);
        decimal vanillaPrice = new EuropeanTreePricer().Price(vanillaCall, market, tree).Price;

        var barrierCall = new BarrierOption(CallPut.Call, 100m, 130m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        PriceResult barrierResult = new BarrierTreePricer().Price(barrierCall, market, tree);

        barrierResult.IsSuccess.Should().BeTrue();
        barrierResult.Price.Should().BeLessThan(vanillaPrice);
        barrierResult.Price.Should().BeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public void KnockInViaParity_ShouldApproximatelyEqualVanillaMinusKnockOut()
    {
        // Barrier parity: V_in + V_out ≈ V_vanilla
        var market = CreateMarket(100m, 0.20, 0.05);
        var tree = BuildTree(market, 100m, 200);

        var vanillaCall = new EuropeanOption(CallPut.Call, 100m, s_expiryDate);
        decimal vanillaPrice = new EuropeanTreePricer().Price(vanillaCall, market, tree).Price;

        var knockOutCall = new BarrierOption(CallPut.Call, 100m, 130m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        var knockInCall = new BarrierOption(CallPut.Call, 100m, 130m, BarrierDirection.Up, BarrierKnockType.In, s_expiryDate);

        decimal knockOutPrice = new BarrierTreePricer().Price(knockOutCall, market, tree).Price;
        decimal knockInPrice = new BarrierTreePricer().Price(knockInCall, market, tree).Price;

        (knockInPrice + knockOutPrice).Should().BeApproximately(vanillaPrice, 0.01m);
    }

    [Fact]
    public void BarrierAtExtremeLevel_ShouldConvergeToVanillaPrice()
    {
        // With an extremely far away barrier, the knock-out option should behave like vanilla
        var market = CreateMarket(100m, 0.20, 0.05);
        var tree = BuildTree(market, 100m, 200);

        var vanillaCall = new EuropeanOption(CallPut.Call, 100m, s_expiryDate);
        decimal vanillaPrice = new EuropeanTreePricer().Price(vanillaCall, market, tree).Price;

        var farBarrierCall = new BarrierOption(CallPut.Call, 100m, 500m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        PriceResult barrierResult = new BarrierTreePricer().Price(farBarrierCall, market, tree);

        barrierResult.Price.Should().BeApproximately(vanillaPrice, 0.01m);
    }

    [Fact]
    public void BarrierAlreadyBreachedAtSpot_ShouldReturnZeroForKnockOut()
    {
        // If the barrier is already breached at spot, knock-out option is worthless
        var market = CreateMarket(100m, 0.20, 0.05);
        var tree = BuildTree(market, 100m, 200);

        // Up barrier at 90 < spot 100 → already breached
        var barrierCall = new BarrierOption(CallPut.Call, 100m, 90m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        PriceResult result = new BarrierTreePricer().Price(barrierCall, market, tree);

        result.Price.Should().Be(0m);
    }

    [Fact]
    public void DownAndOutPut_BarrierParity_ShouldHold()
    {
        // Down barrier parity for puts: V_in + V_out ≈ V_vanilla
        var market = CreateMarket(100m, 0.25, 0.05);
        var tree = BuildTree(market, 100m, 200);

        var vanillaPut = new EuropeanOption(CallPut.Put, 100m, s_expiryDate);
        decimal vanillaPrice = new EuropeanTreePricer().Price(vanillaPut, market, tree).Price;

        var knockOutPut = new BarrierOption(CallPut.Put, 100m, 80m, BarrierDirection.Down, BarrierKnockType.Out, s_expiryDate);
        var knockInPut = new BarrierOption(CallPut.Put, 100m, 80m, BarrierDirection.Down, BarrierKnockType.In, s_expiryDate);

        decimal knockOutPrice = new BarrierTreePricer().Price(knockOutPut, market, tree).Price;
        decimal knockInPrice = new BarrierTreePricer().Price(knockInPut, market, tree).Price;

        (knockInPrice + knockOutPrice).Should().BeApproximately(vanillaPrice, 0.01m);
    }

    private static OptionMarketData CreateMarket(decimal spot, double vol, double rate)
    {
        return new OptionMarketData(
            s_valuationDate,
            spot,
            vol,
            new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, CurrencyCode.USD, rate));
    }

    private static BinomialTree BuildTree(OptionMarketData market, decimal strike, int steps)
    {
        var input = TreeModelInput.FromMarketData(market, strike, s_expiryDate, steps);
        return new CoxRossRubinsteinTreeBuilder().Build(input);
    }
}
