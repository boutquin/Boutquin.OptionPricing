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

namespace Boutquin.OptionPricing.Exotics.Barriers;

/// <summary>
/// Provides Broadie-Glasserman-Kou (BGK) discrete-monitoring corrections that adjust
/// a continuously monitored barrier level to account for the effect of monitoring the
/// barrier at discrete time intervals rather than continuously.
/// </summary>
/// <remarks>
/// The correction formula is:
/// <c>H_adj = H × exp(±β × σ × √(T/m))</c>,
/// where β ≈ 0.5826 (related to the Euler–Mascheroni constant: β = ζ(1/2) / √(2π)),
/// m is the number of monitoring dates, the sign is + for up barriers and − for down barriers.
/// As the number of monitoring dates increases, the adjustment converges to 1.0 (continuous monitoring).
/// </remarks>
public static class BarrierMonitoringAdjustments
{
    /// <summary>
    /// The BGK constant β ≈ 0.5826, derived from ζ(1/2) / √(2π).
    /// </summary>
    private const double Beta = 0.5826;

    /// <summary>
    /// Computes the adjusted barrier level for discrete monitoring using the
    /// Broadie-Glasserman-Kou continuity correction.
    /// </summary>
    /// <param name="barrierLevel">The continuously monitored barrier level. Must be positive.</param>
    /// <param name="direction">
    /// Whether the barrier is an up barrier (adjusted upward) or a down barrier (adjusted downward).
    /// </param>
    /// <param name="volatility">Annualized volatility as a decimal fraction (e.g. 0.20 for 20%). Must be positive.</param>
    /// <param name="timeToExpiry">Time to expiry in years. Must be positive.</param>
    /// <param name="monitoringDates">Number of discrete monitoring dates. Must be at least 1.</param>
    /// <returns>The adjusted barrier level accounting for discrete monitoring.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="barrierLevel"/> is non-positive, <paramref name="volatility"/>
    /// is non-positive, <paramref name="timeToExpiry"/> is non-positive, or
    /// <paramref name="monitoringDates"/> is less than one.
    /// </exception>
    public static decimal AdjustedBarrierLevel(
        decimal barrierLevel,
        BarrierDirection direction,
        double volatility,
        double timeToExpiry,
        int monitoringDates)
    {
        if (barrierLevel <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(barrierLevel), "Barrier level must be positive.");
        }

        if (volatility <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(volatility), "Volatility must be positive.");
        }

        if (timeToExpiry <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToExpiry), "Time to expiry must be positive.");
        }

        if (monitoringDates < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(monitoringDates), "Monitoring dates must be at least 1.");
        }

        double sign = direction == BarrierDirection.Up ? 1d : -1d;
        double exponent = sign * Beta * volatility * Math.Sqrt(timeToExpiry / monitoringDates);
        double adjustmentFactor = Math.Exp(exponent);

        return barrierLevel * (decimal)adjustmentFactor;
    }
}
