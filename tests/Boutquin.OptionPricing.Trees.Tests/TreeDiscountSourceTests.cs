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
using Boutquin.OptionPricing.Trees.Binomial;
using Boutquin.OptionPricing.Trees.Pricing;
using FluentAssertions;

namespace Boutquin.OptionPricing.Trees.Tests;

/// <summary>
/// Pins the discount-rate single-sourcing contract (F-L6-03). The risk-neutral
/// probability <c>p</c> baked into a <see cref="BinomialTree"/> is derived from the
/// build-time discount curve, so the per-step discount applied during backward
/// induction must use the same rate — carried on the tree — rather than a rate
/// re-read from an arbitrary price-time market. Otherwise a tree built from one
/// market and priced with another is silently arbitrage-inconsistent.
/// </summary>
public sealed class TreeDiscountSourceTests
{
    private static readonly DateOnly s_valuationDate = new DateOnly(2026, 6, 15);

    [Fact]
    public void EuropeanPrice_UsesTreeBakedRate_NotPriceTimeMarketRate()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var buildMarket = CreateMarket(0.05d);
        var input = TreeModelInput.FromMarketData(buildMarket, option.Strike.Amount, option.ExpiryDate, 200);
        var tree = new CoxRossRubinsteinTreeBuilder().Build(input);

        // The tree's p was baked from buildMarket's 5% rate. Discounting must come from the
        // tree, so pricing with a divergent 20%-rate market must yield the SAME price as
        // pricing with the build market. Before the fix the discount is re-read from the
        // price-time market, so the two prices differ materially.
        var priceWithBuildMarket = new EuropeanTreePricer().Price(option, buildMarket, tree).Price.Amount;
        var priceWithOtherMarket = new EuropeanTreePricer().Price(option, CreateMarket(0.20d), tree).Price.Amount;

        priceWithOtherMarket.Should().BeApproximately(priceWithBuildMarket, 1e-9m);
    }

    [Fact]
    public void AmericanPrice_UsesTreeBakedRate_NotPriceTimeMarketRate()
    {
        var option = new AmericanOption(CallPut.Put, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var buildMarket = CreateMarket(0.05d);
        var input = TreeModelInput.FromMarketData(buildMarket, option.Strike.Amount, option.ExpiryDate, 200);
        var tree = new CoxRossRubinsteinTreeBuilder().Build(input);

        var priceWithBuildMarket = new AmericanTreePricer().Price(option, buildMarket, tree).Price.Amount;
        var priceWithOtherMarket = new AmericanTreePricer().Price(option, CreateMarket(0.20d), tree).Price.Amount;

        priceWithOtherMarket.Should().BeApproximately(priceWithBuildMarket, 1e-9m);
    }

    private static OptionMarketData CreateMarket(double riskFreeRate)
    {
        return new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(100m),
            0.20d,
            new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, Currency.USD, riskFreeRate),
            new FlatDiscountCurve(new CurveName("DIVIDEND"), s_valuationDate, Currency.USD, 0.00d));
    }
}
