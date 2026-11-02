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

using Boutquin.Domain.Enumerations;
using Boutquin.Domain.Exceptions;
using Boutquin.Domain.Extensions;
using Boutquin.Domain.ValueObjects;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using FluentAssertions;

namespace Boutquin.OptionPricing.Domain.Tests;

/// <summary>
/// Verifies the contract-currency Money model (M-11): the contract carries a
/// <see cref="Currency"/>, the strike is a <see cref="Money"/> whose currency must equal
/// the contract currency, construction with <see cref="Currency.Unspecified"/> is rejected
/// (AC-5b-4), and <c>IntrinsicValue(Money)</c> is currency-guarded (AC-5b §6).
/// </summary>
public sealed class OptionContractCurrencyTests
{
    private static readonly DateOnly s_expiry = new(2026, 12, 19);

    // AC-5b-4 — Unspecified construction guard.
    [Fact]
    public void Construction_WithUnspecifiedCurrency_ShouldThrow()
    {
        Action act = () => _ = new EuropeanOption(CallPut.Call, 100m, Currency.Unspecified, s_expiry);
        act.Should().Throw<ArgumentException>();
    }

    // AC-5b — Strike is exposed as Money carrying the contract currency.
    [Fact]
    public void Strike_IsMoney_InContractCurrency()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiry);
        Money strike = option.Strike;
        strike.Amount.Should().Be(100m);
        strike.Currency.Should().Be(Currency.USD);
        option.Currency.Should().Be(Currency.USD);
    }

    // AC-5b — A Money strike whose currency differs from the contract currency is rejected.
    [Fact]
    public void Strike_WithMismatchedCurrency_ShouldThrow()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiry);
        Action act = () => _ = option with { Strike = Currency.EUR.Amount(100m) };
        act.Should().Throw<ArgumentException>();
    }

    // AC-5b §6 — IntrinsicValue(Money) returns Money in the contract currency.
    [Theory]
    [InlineData(CallPut.Call, 100.0, 110.0, 10.0)]
    [InlineData(CallPut.Call, 100.0, 90.0, 0.0)]
    [InlineData(CallPut.Put, 100.0, 90.0, 10.0)]
    [InlineData(CallPut.Put, 100.0, 110.0, 0.0)]
    public void IntrinsicValue_Money_ReturnsMoneyPayoffInContractCurrency(
        CallPut callPut, double strikeD, double spotD, double expectedD)
    {
        var option = new EuropeanOption(callPut, (decimal)strikeD, Currency.USD, s_expiry);
        Money result = option.IntrinsicValue(Currency.USD.Amount((decimal)spotD));
        result.Currency.Should().Be(Currency.USD);
        result.Amount.Should().Be((decimal)expectedD);
    }

    // AC-5b §6 — IntrinsicValue throws when the spot currency differs from the contract currency.
    [Fact]
    public void IntrinsicValue_MismatchedSpotCurrency_ShouldThrowCurrencyMismatch()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiry);
        Action act = () => option.IntrinsicValue(Currency.EUR.Amount(110m));
        act.Should().Throw<CurrencyMismatchException>();
    }

    // CCY-DESYNC-WITH-001 — `with { Currency = ... }` must not desync Currency from Strike.Currency.
    [Fact]
    public void With_CurrencyDivergingFromStrike_ShouldThrow()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiry);
        Action act = () => _ = option with { Currency = Currency.EUR };
        act.Should().Throw<ArgumentException>();
    }

    // CCY-DESYNC-WITH-001 — a desynced contract must never silently compute a cross-currency payoff.
    [Fact]
    public void With_CurrencyDesync_IntrinsicValueShouldThrowNotComputeSilently()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiry);

        // Reproduce the headline bug: re-set only Currency, leaving Strike in USD.
        // Either the `with` itself throws (desired guard), or — if it ever slipped through —
        // IntrinsicValue must not silently return a cross-currency 50 EUR payoff.
        Action act = () =>
        {
            var desynced = option with { Currency = Currency.EUR };
            _ = desynced.IntrinsicValue(Currency.EUR.Amount(150m));
        };
        act.Should().Throw<ArgumentException>();
    }

    // CCY-UNSPEC-WITH-002 — `with { Currency = Unspecified }` must be rejected (guard not only in ctor).
    [Fact]
    public void With_UnspecifiedCurrency_ShouldThrow()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiry);
        Action act = () => _ = option with { Currency = Currency.Unspecified };
        act.Should().Throw<ArgumentException>();
    }
}
