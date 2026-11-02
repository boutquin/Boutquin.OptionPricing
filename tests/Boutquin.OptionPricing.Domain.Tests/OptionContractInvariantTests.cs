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
using Boutquin.Domain.Extensions;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using FluentAssertions;

namespace Boutquin.OptionPricing.Domain.Tests;

/// <summary>
/// Verifies that the Strike and BarrierLevel positive invariants cannot be bypassed
/// via <c>with</c> expressions, and that IntrinsicValue payoff is correctly computed.
/// Findings 018, 019, 020.
/// </summary>
public sealed class OptionContractInvariantTests
{
    private static readonly DateOnly s_expiry = new(2026, 12, 19);

    // -----------------------------------------------------------------------
    // Finding 018 — OptionContract.Strike: with-expression bypass
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(-50.0)]
    public void Strike_WithExpression_NegativeOrZero_ShouldThrowArgumentOutOfRangeException(double badStrikeD)
    {
        var badStrike = (decimal)badStrikeD;
        // Arrange — a valid option constructed via the normal ctor
        var valid = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiry);

        // Act — mutate Strike via a with-expression to a non-positive value
        Action act = () => _ = valid with { Strike = Currency.USD.Amount(badStrike) };

        // Assert — must throw, not silently produce an invalid contract
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value");
    }

    [Fact]
    public void Strike_WithExpression_PositiveValue_ShouldNotThrow()
    {
        var valid = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiry);
        Action act = () => _ = valid with { Strike = Currency.USD.Amount(50m) };
        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Finding 019 — BarrierOption.BarrierLevel: with-expression bypass
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(-100.0)]
    public void BarrierLevel_WithExpression_NegativeOrZero_ShouldThrowArgumentOutOfRangeException(double badLevelD)
    {
        var badLevel = (decimal)badLevelD;
        // Arrange — a valid barrier option
        var valid = new BarrierOption(CallPut.Call, 100m, Currency.USD, 120m,
            BarrierDirection.Up, BarrierKnockType.Out, s_expiry);

        // Act — mutate BarrierLevel via a with-expression to a non-positive value
        Action act = () => _ = valid with { BarrierLevel = badLevel };

        // Assert — must throw
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value");
    }

    [Fact]
    public void BarrierLevel_WithExpression_PositiveValue_ShouldNotThrow()
    {
        // This mirrors the live mutation at BarrierTreePricer.cs:132 — a positive
        // adjustment must still be accepted.
        var valid = new BarrierOption(CallPut.Call, 100m, Currency.USD, 120m,
            BarrierDirection.Up, BarrierKnockType.Out, s_expiry);
        Action act = () => _ = valid with { BarrierLevel = 115m };
        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Finding 020 — IntrinsicValue: direct unit-test coverage
    // -----------------------------------------------------------------------

    // InlineData cannot use decimal literals directly; parameters are passed as double
    // and cast to decimal inside the test body.
    [Theory]
    // Call ITM: spot > strike → max(S-K,0) = S-K
    [InlineData(CallPut.Call, 100.0, 110.0, 10.0)]
    // Call OTM: spot < strike → 0
    [InlineData(CallPut.Call, 100.0, 90.0, 0.0)]
    // Call ATM: spot == strike → 0
    [InlineData(CallPut.Call, 100.0, 100.0, 0.0)]
    // Put ITM: spot < strike → max(K-S,0) = K-S
    [InlineData(CallPut.Put, 100.0, 90.0, 10.0)]
    // Put OTM: spot > strike → 0
    [InlineData(CallPut.Put, 100.0, 110.0, 0.0)]
    // Put ATM: spot == strike → 0
    [InlineData(CallPut.Put, 100.0, 100.0, 0.0)]
    public void IntrinsicValue_ShouldReturnCorrectPayoff(
        CallPut callPut, double strikeD, double spotD, double expectedPayoffD)
    {
        var strike = (decimal)strikeD;
        var spot = (decimal)spotD;
        var expectedPayoff = (decimal)expectedPayoffD;
        var option = new EuropeanOption(callPut, strike, Currency.USD, s_expiry);
        option.IntrinsicValue(Currency.USD.Amount(spot)).Amount.Should().Be(expectedPayoff);
    }

    [Fact]
    public void IntrinsicValue_ZeroSpot_ShouldThrowArgumentOutOfRangeException()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiry);
        Action act = () => option.IntrinsicValue(Currency.USD.Amount(0m));
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("spot");
    }

    [Fact]
    public void IntrinsicValue_NegativeSpot_ShouldThrowArgumentOutOfRangeException()
    {
        var option = new EuropeanOption(CallPut.Put, 100m, Currency.USD, s_expiry);
        Action act = () => option.IntrinsicValue(Currency.USD.Amount(-5m));
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("spot");
    }
}
