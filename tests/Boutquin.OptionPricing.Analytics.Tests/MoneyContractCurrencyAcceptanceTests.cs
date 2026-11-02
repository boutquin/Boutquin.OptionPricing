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
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Domain.Results;
using FluentAssertions;

namespace Boutquin.OptionPricing.Analytics.Tests;

/// <summary>
/// Milestone M-11 acceptance tests for the contract-currency Money model in OptionPricing:
/// the same-currency boundary invariant (AC-5b), the failure-path currency contract of
/// <see cref="PriceResult.Unsupported(string, string, Currency)"/> (AC-5b-5), and the
/// numeric-invariance golden vector that pins <see cref="PriceResult.Price"/> byte-identical
/// to the pre-Money baseline (AC-5b numeric invariance / AC-5b-1 engine purity).
/// </summary>
public sealed class MoneyContractCurrencyAcceptanceTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 15);

    private static OptionMarketData CreateMarket(decimal spot, Currency currency)
        => new(
            s_valuationDate,
            currency.Amount(spot),
            0.20d,
            new FlatDiscountCurve(new CurveName("OIS"), s_valuationDate, currency, 0.05d),
            new FlatDiscountCurve(new CurveName("DIV"), s_valuationDate, currency, 0.00d));

    // AC-5b — a strike and spot/market in different currencies throw CurrencyMismatchException
    // from the public pricing API (here BlackScholesPricer.Price, a real entry point).
    [Fact]
    public void Price_CrossCurrencyStrikeVsSpot_ThrowsCurrencyMismatch()
    {
        var usdOption = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddDays(365));
        var eurMarket = CreateMarket(100m, Currency.EUR);

        Action act = () => new BlackScholesPricer().Price(usdOption, eurMarket);

        act.Should().Throw<CurrencyMismatchException>();
    }

    // AC-5b — the same input in matching currencies does NOT throw and returns a Money premium
    // denominated in the contract currency.
    [Fact]
    public void Price_SameCurrency_ReturnsPremiumInContractCurrency()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddDays(365));
        var market = CreateMarket(100m, Currency.USD);

        PriceResult result = new BlackScholesPricer().Price(option, market);

        result.Price.Currency.Should().Be(Currency.USD);
        result.Price.Amount.Should().BeGreaterThan(0m);
    }

    // AC-5b-5 — Unsupported returns a zero amount IN the contract currency (never Unspecified),
    // and summing that zero with a same-currency real premium does NOT throw.
    [Fact]
    public void Unsupported_ReturnsZeroInContractCurrency_AndSumsWithPremium()
    {
        PriceResult unsupported = PriceResult.Unsupported("AnyModel", "any reason", Currency.USD);

        unsupported.Price.Amount.Should().Be(0m);
        unsupported.Price.Currency.Should().Be(Currency.USD);

        Money premium = Currency.USD.Amount(12.34m);
        Money total = unsupported.Price + premium;

        total.Should().Be(Currency.USD.Amount(12.34m));
    }

    // AC-5b-5 (negative direction) — Money values must always carry a defined currency;
    // even zero amounts using Currency.Unspecified are rejected at construction.
    [Fact]
    public void Unspecified_Zero_IsRejectedAtConstruction()
    {
        Action act = () => _ = Currency.Unspecified.Amount(0m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // AC-5b numeric invariance / AC-5b-1 — the Black-Scholes premium amount for a fixed
    // same-currency input is BYTE-IDENTICAL to the pre-Money baseline captured from the
    // pre-change GREEN code. The engine stays double; Money only wraps at the boundary.
    [Fact]
    public void GoldenVector_BlackScholesPremium_IsByteIdenticalToPreMoneyBaseline()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddDays(365));
        var market = CreateMarket(100m, Currency.USD);

        Money premium = new BlackScholesPricer().Price(option, market).Price;

        // Captured from the pre-Money GREEN code (spot=100, K=100, T=365d, r=5%, q=0%, vol=20%).
        premium.Amount.Should().Be(10.4505835721856m);
        premium.Currency.Should().Be(Currency.USD);
    }
}
