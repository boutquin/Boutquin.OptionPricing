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
using Boutquin.Domain.Exceptions;
using Boutquin.Domain.Extensions;
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

        var vanillaPut = new EuropeanOption(CallPut.Put, 100m, Currency.USD, s_expiryDate);
        decimal vanillaPrice = new EuropeanTreePricer().Price(vanillaPut, market, tree).Price.Amount;

        var barrierPut = new BarrierOption(CallPut.Put, 100m, Currency.USD, 80m, BarrierDirection.Down, BarrierKnockType.Out, s_expiryDate);
        PriceResult barrierResult = new BarrierTreePricer().Price(barrierPut, market, tree);

        barrierResult.IsSuccess.Should().BeTrue();
        barrierResult.Price.Amount.Should().BeLessThan(vanillaPrice);
        barrierResult.Price.Amount.Should().BeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public void UpAndOutCall_ShouldBeCheaperThanVanillaCall()
    {
        // An up-and-out call can be knocked out, so it should cost less than vanilla
        var market = CreateMarket(100m, 0.20, 0.05);
        var tree = BuildTree(market, 100m, 200);

        var vanillaCall = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiryDate);
        decimal vanillaPrice = new EuropeanTreePricer().Price(vanillaCall, market, tree).Price.Amount;

        var barrierCall = new BarrierOption(CallPut.Call, 100m, Currency.USD, 130m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        PriceResult barrierResult = new BarrierTreePricer().Price(barrierCall, market, tree);

        barrierResult.IsSuccess.Should().BeTrue();
        barrierResult.Price.Amount.Should().BeLessThan(vanillaPrice);
        barrierResult.Price.Amount.Should().BeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public void KnockInViaParity_ShouldApproximatelyEqualVanillaMinusKnockOut()
    {
        // Barrier parity: V_in + V_out ≈ V_vanilla
        var market = CreateMarket(100m, 0.20, 0.05);
        var tree = BuildTree(market, 100m, 200);

        var vanillaCall = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiryDate);
        decimal vanillaPrice = new EuropeanTreePricer().Price(vanillaCall, market, tree).Price.Amount;

        var knockOutCall = new BarrierOption(CallPut.Call, 100m, Currency.USD, 130m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        var knockInCall = new BarrierOption(CallPut.Call, 100m, Currency.USD, 130m, BarrierDirection.Up, BarrierKnockType.In, s_expiryDate);

        decimal knockOutPrice = new BarrierTreePricer().Price(knockOutCall, market, tree).Price.Amount;
        decimal knockInPrice = new BarrierTreePricer().Price(knockInCall, market, tree).Price.Amount;

        (knockInPrice + knockOutPrice).Should().BeApproximately(vanillaPrice, 0.01m);
    }

    [Fact]
    public void BarrierAtExtremeLevel_ShouldConvergeToVanillaPrice()
    {
        // With an extremely far away barrier, the knock-out option should behave like vanilla
        var market = CreateMarket(100m, 0.20, 0.05);
        var tree = BuildTree(market, 100m, 200);

        var vanillaCall = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiryDate);
        decimal vanillaPrice = new EuropeanTreePricer().Price(vanillaCall, market, tree).Price.Amount;

        var farBarrierCall = new BarrierOption(CallPut.Call, 100m, Currency.USD, 500m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        PriceResult barrierResult = new BarrierTreePricer().Price(farBarrierCall, market, tree);

        barrierResult.Price.Amount.Should().BeApproximately(vanillaPrice, 0.01m);
    }

    [Fact]
    public void BarrierAlreadyBreachedAtSpot_ShouldReturnZeroForKnockOut()
    {
        // If the barrier is already breached at spot, knock-out option is worthless
        var market = CreateMarket(100m, 0.20, 0.05);
        var tree = BuildTree(market, 100m, 200);

        // Up barrier at 90 < spot 100 → already breached
        var barrierCall = new BarrierOption(CallPut.Call, 100m, Currency.USD, 90m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        PriceResult result = new BarrierTreePricer().Price(barrierCall, market, tree);

        result.Price.Amount.Should().Be(0m);
    }

    [Fact]
    public void DownAndOutPut_BarrierParity_ShouldHold()
    {
        // Down barrier parity for puts: V_in + V_out ≈ V_vanilla
        var market = CreateMarket(100m, 0.25, 0.05);
        var tree = BuildTree(market, 100m, 200);

        var vanillaPut = new EuropeanOption(CallPut.Put, 100m, Currency.USD, s_expiryDate);
        decimal vanillaPrice = new EuropeanTreePricer().Price(vanillaPut, market, tree).Price.Amount;

        var knockOutPut = new BarrierOption(CallPut.Put, 100m, Currency.USD, 80m, BarrierDirection.Down, BarrierKnockType.Out, s_expiryDate);
        var knockInPut = new BarrierOption(CallPut.Put, 100m, Currency.USD, 80m, BarrierDirection.Down, BarrierKnockType.In, s_expiryDate);

        decimal knockOutPrice = new BarrierTreePricer().Price(knockOutPut, market, tree).Price.Amount;
        decimal knockInPrice = new BarrierTreePricer().Price(knockInPut, market, tree).Price.Amount;

        (knockInPrice + knockOutPrice).Should().BeApproximately(vanillaPrice, 0.01m);
    }

    [Fact]
    public void Price_ShouldDiscountWithTreeRateNotMarketRate_WhenTreeBuiltAtDifferentRate()
    {
        // Fix 007 single-sourced discounting through tree.RiskFreeRate ?? market for the
        // vanilla tree pricers. The barrier kernel must match: a barrier priced on a tree
        // built at rate A but with a market carrying rate B must discount at A (the rate
        // embedded in the tree's probabilities), exactly like EuropeanTreePricer on the
        // same tree — not at the at-pricing-time market rate B.
        var buildMarket = CreateMarket(100m, 0.20, 0.05);   // rate A = 0.05 → baked into tree
        var pricingMarket = CreateMarket(100m, 0.20, 0.15);  // rate B = 0.15 → must be ignored
        var tree = BuildTree(buildMarket, 100m, 200);

        // European price on this tree uses tree.RiskFreeRate (A), regardless of market B.
        var vanillaCall = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiryDate);
        decimal europeanPrice = new EuropeanTreePricer().Price(vanillaCall, pricingMarket, tree).Price.Amount;

        // A never-breached knock-out call (barrier far above any reachable spot) reduces to
        // the vanilla payoff, so its tree price must equal the European tree price when both
        // discount at the same (tree) rate.
        var farBarrierCall = new BarrierOption(CallPut.Call, 100m, Currency.USD, 100_000m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        decimal barrierPrice = new BarrierTreePricer().Price(farBarrierCall, pricingMarket, tree).Price.Amount;

        barrierPrice.Should().BeApproximately(europeanPrice, 0.0001m,
            "the barrier kernel must discount with tree.RiskFreeRate (A), matching the European pricer, not market rate B");
    }

    // AC-5b — characterization test: BarrierTreePricer.PriceDiscretelyMonitored throws
    // CurrencyMismatchException when option currency (USD) differs from market spot currency (EUR).
    // PriceDiscretelyMonitored delegates to Price() which carries the currency boundary guard.
    // This pins that the secondary entry point propagates the exception, warranting the
    // <exception cref="CurrencyMismatchException"/> doc tag (DOC-CCYMISMATCH-DISCMON-007).
    [Fact]
    public void PriceDiscretelyMonitored_CrossCurrencySpotVsOption_ThrowsCurrencyMismatchException()
    {
        var usdOption = new BarrierOption(CallPut.Call, 100m, Currency.USD, 130m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        var eurMarket = new OptionMarketData(
            s_valuationDate,
            Currency.EUR.Amount(100m),
            0.20d,
            new FlatDiscountCurve(new CurveName("EUR-OIS"), s_valuationDate, Currency.EUR, 0.05d));
        var eurTree = new CoxRossRubinsteinTreeBuilder().Build(
            TreeModelInput.FromMarketData(eurMarket, usdOption.Strike.Amount, s_expiryDate, 50));

        Action act = () => new BarrierTreePricer().PriceDiscretelyMonitored(usdOption, eurMarket, eurTree, 12);

        act.Should().Throw<CurrencyMismatchException>();
    }

    private static OptionMarketData CreateMarket(decimal spot, double vol, double rate)
    {
        return new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(spot),
            vol,
            new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, Currency.USD, rate));
    }

    private static BinomialTree BuildTree(OptionMarketData market, decimal strike, int steps)
    {
        var input = TreeModelInput.FromMarketData(market, strike, s_expiryDate, steps);
        return new CoxRossRubinsteinTreeBuilder().Build(input);
    }
}
