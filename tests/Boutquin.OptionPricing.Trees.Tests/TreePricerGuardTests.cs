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
/// Null-argument guards on the tree pricers' public <c>Price</c> entry points
/// (F-L23-01 / F-L23-02 / F-L5-03). The <c>Price</c> methods must reject null
/// <c>option</c>, <c>market</c>, and <c>tree</c> with <see cref="ArgumentNullException"/>,
/// matching the contract already enforced by
/// <see cref="AmericanTreePricer.ComputeExerciseBoundary"/>, rather than faulting with a
/// raw <see cref="NullReferenceException"/> on the first dereference.
/// </summary>
public sealed class TreePricerGuardTests
{
    private static readonly DateOnly s_valuationDate = new DateOnly(2026, 6, 15);

    [Fact]
    public void EuropeanPrice_NullOption_ThrowsArgumentNullException()
    {
        var (market, tree) = BuildContext();
        var pricer = new EuropeanTreePricer();

        Action act = () => pricer.Price(null!, market, tree);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("option");
    }

    [Fact]
    public void EuropeanPrice_NullMarket_ThrowsArgumentNullException()
    {
        var (_, tree) = BuildContext();
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var pricer = new EuropeanTreePricer();

        Action act = () => pricer.Price(option, null!, tree);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("market");
    }

    [Fact]
    public void EuropeanPrice_NullTree_ThrowsArgumentNullException()
    {
        var (market, _) = BuildContext();
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var pricer = new EuropeanTreePricer();

        Action act = () => pricer.Price(option, market, null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("tree");
    }

    [Fact]
    public void AmericanPrice_NullOption_ThrowsArgumentNullException()
    {
        var (market, tree) = BuildContext();
        var pricer = new AmericanTreePricer();

        Action act = () => pricer.Price(null!, market, tree);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("option");
    }

    [Fact]
    public void AmericanPrice_NullMarket_ThrowsArgumentNullException()
    {
        var (_, tree) = BuildContext();
        var option = new AmericanOption(CallPut.Put, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var pricer = new AmericanTreePricer();

        Action act = () => pricer.Price(option, null!, tree);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("market");
    }

    [Fact]
    public void AmericanPrice_NullTree_ThrowsArgumentNullException()
    {
        var (market, _) = BuildContext();
        var option = new AmericanOption(CallPut.Put, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var pricer = new AmericanTreePricer();

        Action act = () => pricer.Price(option, market, null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("tree");
    }

    private static (OptionMarketData Market, BinomialTree Tree) BuildContext()
    {
        var market = new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(100m),
            0.20d,
            new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, Currency.USD, 0.05d),
            new FlatDiscountCurve(new CurveName("DIVIDEND"), s_valuationDate, Currency.USD, 0.00d));
        var input = TreeModelInput.FromMarketData(market, 100m, s_valuationDate.AddMonths(6), 50);
        var tree = new CoxRossRubinsteinTreeBuilder().Build(input);
        return (market, tree);
    }
}
