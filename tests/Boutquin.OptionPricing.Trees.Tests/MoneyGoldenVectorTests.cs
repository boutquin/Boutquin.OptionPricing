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
using Boutquin.Domain.ValueObjects;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Trees.Binomial;
using Boutquin.OptionPricing.Trees.Pricing;
using FluentAssertions;

namespace Boutquin.OptionPricing.Trees.Tests;

/// <summary>
/// Milestone M-11 numeric-invariance golden vectors for the tree pricers (AC-5b numeric
/// invariance / AC-5b-1 engine purity): the premium amount for a fixed same-currency input
/// is byte-identical to the pre-Money baseline captured from the pre-change GREEN code, and
/// the same-currency boundary invariant throws on a cross-currency strike vs spot.
/// </summary>
public sealed class MoneyGoldenVectorTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 15);

    private static OptionMarketData CreateMarket(decimal spot, Currency currency)
        => new(
            s_valuationDate,
            currency.Amount(spot),
            0.20d,
            new FlatDiscountCurve(new CurveName("OIS"), s_valuationDate, currency, 0.05d),
            new FlatDiscountCurve(new CurveName("DIV"), s_valuationDate, currency, 0.00d));

    [Fact]
    public void GoldenVector_EuropeanTreeCall_IsByteIdenticalToPreMoneyBaseline()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddDays(365));
        var market = CreateMarket(100m, Currency.USD);
        var input = TreeModelInput.FromMarketData(market, option.Strike.Amount, option.ExpiryDate, 256);
        var tree = new CoxRossRubinsteinTreeBuilder().Build(input);

        Money premium = new EuropeanTreePricer().Price(option, market, tree).Price;

        // 256-step CRR, spot=100, K=100, T=365d, r=5%, q=0%, vol=20%.
        premium.Amount.Should().Be(10.4427759998244m);
        premium.Currency.Should().Be(Currency.USD);
    }

    [Fact]
    public void GoldenVector_AmericanTreePut_IsByteIdenticalToPreMoneyBaseline()
    {
        var option = new AmericanOption(CallPut.Put, 100m, Currency.USD, s_valuationDate.AddDays(365));
        var market = CreateMarket(100m, Currency.USD);
        var input = TreeModelInput.FromMarketData(market, option.Strike.Amount, option.ExpiryDate, 256);
        var tree = new CoxRossRubinsteinTreeBuilder().Build(input);

        Money premium = new AmericanTreePricer().Price(option, market, tree).Price;

        // 256-step CRR, spot=100, K=100, T=365d, r=5%, q=0%, vol=20%.
        premium.Amount.Should().Be(6.08725832770924m);
        premium.Currency.Should().Be(Currency.USD);
    }

    [Fact]
    public void EuropeanTreePrice_CrossCurrencyStrikeVsSpot_ThrowsCurrencyMismatch()
    {
        var usdOption = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddDays(365));
        var usdMarket = CreateMarket(100m, Currency.USD);
        var input = TreeModelInput.FromMarketData(usdMarket, usdOption.Strike.Amount, usdOption.ExpiryDate, 64);
        var tree = new CoxRossRubinsteinTreeBuilder().Build(input);
        var eurMarket = CreateMarket(100m, Currency.EUR);

        Action act = () => new EuropeanTreePricer().Price(usdOption, eurMarket, tree);

        act.Should().Throw<CurrencyMismatchException>();
    }
}
