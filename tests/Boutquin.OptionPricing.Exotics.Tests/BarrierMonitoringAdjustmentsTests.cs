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

using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Exotics.Barriers;
using FluentAssertions;

namespace Boutquin.OptionPricing.Exotics.Tests;

/// <summary>
/// Tests for the Broadie-Glasserman-Kou discrete-monitoring barrier level adjustment.
/// Formula: H_adj = H × exp(±β × σ × √(T/m)), β ≈ 0.5826.
/// </summary>
public sealed class BarrierMonitoringAdjustmentsTests
{
    [Fact]
    public void AdjustedBarrierLevel_UpBarrier_ShouldBeHigherThanOriginal()
    {
        // Up barrier: adjusted level shifts upward (positive exponent)
        decimal adjusted = BarrierMonitoringAdjustments.AdjustedBarrierLevel(
            barrierLevel: 120m,
            direction: BarrierDirection.Up,
            volatility: 0.20,
            timeToExpiry: 1.0,
            monitoringDates: 252);

        adjusted.Should().BeGreaterThan(120m);
    }

    [Fact]
    public void AdjustedBarrierLevel_DownBarrier_ShouldBeLowerThanOriginal()
    {
        // Down barrier: adjusted level shifts downward (negative exponent)
        decimal adjusted = BarrierMonitoringAdjustments.AdjustedBarrierLevel(
            barrierLevel: 80m,
            direction: BarrierDirection.Down,
            volatility: 0.20,
            timeToExpiry: 1.0,
            monitoringDates: 252);

        adjusted.Should().BeLessThan(80m);
    }

    [Fact]
    public void AdjustedBarrierLevel_ContinuousLimit_ShouldApproachOriginalLevel()
    {
        // As monitoring dates → ∞, √(T/m) → 0 and adjustment → 1.0
        decimal adjusted = BarrierMonitoringAdjustments.AdjustedBarrierLevel(
            barrierLevel: 100m,
            direction: BarrierDirection.Up,
            volatility: 0.20,
            timeToExpiry: 1.0,
            monitoringDates: 1_000_000);

        adjusted.Should().BeApproximately(100m, 0.02m);
    }

    [Fact]
    public void AdjustedBarrierLevel_KnownPublishedExample_ShouldMatchExpected()
    {
        // BGK example: H=100, σ=0.30, T=1, m=12 (monthly monitoring)
        // β = 0.5826
        // √(T/m) = √(1/12) = 0.28868
        // β × σ × √(T/m) = 0.5826 × 0.30 × 0.28868 = 0.05044
        // exp(0.05044) = 1.05173
        // H_adj = 100 × 1.05173 = 105.173
        decimal adjusted = BarrierMonitoringAdjustments.AdjustedBarrierLevel(
            barrierLevel: 100m,
            direction: BarrierDirection.Up,
            volatility: 0.30,
            timeToExpiry: 1.0,
            monitoringDates: 12);

        adjusted.Should().BeApproximately(105.17m, 0.05m);
    }

    [Fact]
    public void AdjustedBarrierLevel_DownBarrier_KnownExample_ShouldMatchExpected()
    {
        // Same parameters as above but down barrier: exp(-0.05044) = 0.9508
        // H_adj = 100 × 0.9508 = 95.08
        decimal adjusted = BarrierMonitoringAdjustments.AdjustedBarrierLevel(
            barrierLevel: 100m,
            direction: BarrierDirection.Down,
            volatility: 0.30,
            timeToExpiry: 1.0,
            monitoringDates: 12);

        adjusted.Should().BeApproximately(95.08m, 0.05m);
    }

    // ── RED test for F-L23-03 (017): unrecognized BarrierDirection must throw, not silently treat as Down ──
    [Fact]
    public void AdjustedBarrierLevel_UnrecognizedDirection_Throws()
    {
        // (BarrierDirection)999 is an unrecognized value. The sibling method IsBarrierBreached
        // throws NotSupportedException; AdjustedBarrierLevel must match the family contract.
        // The undefined value must not be assigned the valid Down-barrier sign.
        Action act = () => BarrierMonitoringAdjustments.AdjustedBarrierLevel(
            barrierLevel: 100m,
            direction: (BarrierDirection)999,
            volatility: 0.20,
            timeToExpiry: 1.0,
            monitoringDates: 12);

        act.Should().Throw<NotSupportedException>(
            "an unrecognized BarrierDirection must throw rather than silently applying Down-barrier sign");
    }
}
