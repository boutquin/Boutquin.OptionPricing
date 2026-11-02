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

using Boutquin.OptionPricing.Analytics.Parity;
using FluentAssertions;

namespace Boutquin.OptionPricing.Analytics.Tests;

/// <summary>
/// Guard tests for <see cref="RiskNeutralValuation.ForwardPrice(double, double, double, double)"/>
/// raw-factor overload (F-L23-01: divide-by-zero / silent-infinity regression).
/// </summary>
public sealed class RiskNeutralValuationGuardTests
{
    // --- F-L23-01 regression: zero discountFactor silently produced +Infinity ---

    [Fact]
    public void ForwardPrice_ZeroDiscountFactor_ThrowsArgumentOutOfRange()
    {
        // Prior to fix: 100 * 0.95 * 0.98 / 0.0 == +Infinity, no exception.
        // Expected after fix: ArgumentOutOfRangeException on discountFactor <= 0.
        Action act = () => RiskNeutralValuation.ForwardPrice(
            spot: 100d,
            discountFactor: 0.0d,
            dividendDiscountFactor: 0.95d,
            borrowDiscountFactor: 0.98d);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .And.ParamName.Should().Be("discountFactor");
    }

    [Fact]
    public void ForwardPrice_NegativeDiscountFactor_ThrowsArgumentOutOfRange()
    {
        Action act = () => RiskNeutralValuation.ForwardPrice(
            spot: 100d,
            discountFactor: -0.5d);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .And.ParamName.Should().Be("discountFactor");
    }

    [Fact]
    public void ForwardPrice_ZeroSpot_ThrowsArgumentOutOfRange()
    {
        Action act = () => RiskNeutralValuation.ForwardPrice(
            spot: 0d,
            discountFactor: 0.95d);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .And.ParamName.Should().Be("spot");
    }

    [Fact]
    public void ForwardPrice_NegativeSpot_ThrowsArgumentOutOfRange()
    {
        Action act = () => RiskNeutralValuation.ForwardPrice(
            spot: -10d,
            discountFactor: 0.95d);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .And.ParamName.Should().Be("spot");
    }

    // --- Healthy-path: result must be finite ---

    [Fact]
    public void ForwardPrice_ValidInputs_ReturnsFinitePositiveValue()
    {
        double result = RiskNeutralValuation.ForwardPrice(
            spot: 100d,
            discountFactor: 0.95d,
            dividendDiscountFactor: 0.98d,
            borrowDiscountFactor: 1.0d);

        double.IsFinite(result).Should().BeTrue("forward price must never be ±Infinity or NaN");
        result.Should().BeGreaterThan(0d);
    }
}
