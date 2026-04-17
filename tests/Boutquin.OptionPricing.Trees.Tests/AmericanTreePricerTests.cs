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
using Boutquin.OptionPricing.Trees.Binomial;
using Boutquin.OptionPricing.Trees.Pricing;
using FluentAssertions;

namespace Boutquin.OptionPricing.Trees.Tests;

/// <summary>
/// Tests for <see cref="AmericanTreePricer"/> validating early exercise behavior,
/// convergence, and boundary conditions against known option pricing properties.
/// </summary>
public sealed class AmericanTreePricerTests
{
    [Fact]
    public void AmericanPut_ShouldExceedEuropeanPut()
    {
        // American put >= European put due to early exercise premium
        DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
        var americanPut = new AmericanOption(CallPut.Put, 100m, valuationDate.AddMonths(6));
        var europeanPut = new EuropeanOption(CallPut.Put, 100m, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.05d, 0.00d);

        var tree = BuildTree(market, americanPut, 200);
        var americanPrice = new AmericanTreePricer().Price(americanPut, market, tree).Price;
        var europeanPrice = new EuropeanTreePricer().Price(europeanPut, market, tree).Price;

        americanPrice.Should().BeGreaterThanOrEqualTo(europeanPrice);
    }

    [Fact]
    public void AmericanCall_NoDividend_ShouldEqualEuropeanCall()
    {
        // Without dividends, never optimal to exercise American call early
        DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
        var americanCall = new AmericanOption(CallPut.Call, 100m, valuationDate.AddMonths(6));
        var europeanCall = new EuropeanOption(CallPut.Call, 100m, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.00d);

        var tree = BuildTree(market, americanCall, 400);
        var americanPrice = new AmericanTreePricer().Price(americanCall, market, tree).Price;
        var europeanPrice = new EuropeanTreePricer().Price(europeanCall, market, tree).Price;

        americanPrice.Should().BeApproximately(europeanPrice, 0.05m);
    }

    [Fact]
    public void AmericanPut_ShouldConvergeWithMoreSteps()
    {
        // Price difference between coarse and fine trees should shrink
        DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
        var option = new AmericanOption(CallPut.Put, 100m, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.00d);

        var tree100 = BuildTree(market, option, 100);
        var tree400 = BuildTree(market, option, 400);
        var price100 = new AmericanTreePricer().Price(option, market, tree100).Price;
        var price400 = new AmericanTreePricer().Price(option, market, tree400).Price;

        // Both prices should be close; the fine tree should be closer to the limit
        Math.Abs(price400 - price100).Should().BeLessThan(0.50m);
    }

    [Fact]
    public void AmericanPut_DeepInTheMoney_ShouldApproachIntrinsic()
    {
        // Deep ITM put (S=50, K=100): price should be near intrinsic = 50
        DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
        var option = new AmericanOption(CallPut.Put, 100m, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 50m, 0.20d, 0.03d, 0.00d);

        var tree = BuildTree(market, option, 200);
        var price = new AmericanTreePricer().Price(option, market, tree).Price;

        // Price should be at least intrinsic (50) and close to it
        price.Should().BeGreaterThanOrEqualTo(50m);
        price.Should().BeLessThan(52m);
    }

    private static BinomialTree BuildTree(OptionMarketData market, OptionContract option, int steps)
    {
        var input = TreeModelInput.FromMarketData(market, option.Strike, option.ExpiryDate, steps);
        return new CoxRossRubinsteinTreeBuilder().Build(input);
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
