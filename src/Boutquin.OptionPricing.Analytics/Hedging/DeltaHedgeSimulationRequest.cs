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

using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Market;

namespace Boutquin.OptionPricing.Analytics.Hedging;

/// <summary>
/// Encapsulates every input needed to run a discrete delta-hedge simulation along a
/// single realized spot path for a European option.
/// </summary>
/// <param name="Option">European option whose payoff will be replicated by the hedge.</param>
/// <param name="InitialMarket">
/// Market snapshot at trade inception, supplying the initial spot, curves, and volatility
/// used to compute the starting Black-Scholes price and first delta hedge ratio.
/// </param>
/// <param name="PathSpots">
/// Chronologically ordered spot prices observed at each rebalance date.
/// The first element is the spot at inception; the last is the spot at expiry.
/// </param>
public sealed record DeltaHedgeSimulationRequest(
    EuropeanOption Option,
    OptionMarketData InitialMarket,
    IReadOnlyList<decimal> PathSpots)
{
    /// <summary>Owned read-only snapshot of the realized path from inception to expiry.</summary>
    public IReadOnlyList<decimal> PathSpots { get; init => field = Copy(value); } = Copy(PathSpots);

    private static IReadOnlyList<decimal> Copy(IReadOnlyList<decimal> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.ToArray());
    }
}
