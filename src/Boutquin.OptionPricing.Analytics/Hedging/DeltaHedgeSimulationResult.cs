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

using Boutquin.OptionPricing.Abstractions;

namespace Boutquin.OptionPricing.Analytics.Hedging;

/// <summary>
/// Captures the output of a discrete delta-hedge simulation: the initial option price,
/// the sequence of delta hedge ratios, and the realized spot path.
/// </summary>
/// <param name="StartingOptionPrice">
/// Black-Scholes fair value at trade inception, representing the premium collected
/// (or paid) to initiate the hedge.
/// </param>
/// <param name="Deltas">
/// Delta hedge ratio at each rebalance date. Each value represents the number of
/// underlying shares held to replicate the option payoff.
/// </param>
/// <param name="Spots">
/// Realized spot prices at each rebalance date, echoed from the request for
/// downstream P/L attribution.
/// </param>
/// <param name="OptionPayoff">
/// The option's intrinsic payoff at expiry, computed from the terminal spot price.
/// </param>
/// <param name="FinalPortfolioValue">
/// The terminal value of the self-financing hedging portfolio (shares held × terminal
/// spot + cash position grown at the risk-free rate).
/// </param>
/// <param name="HedgingError">
/// The difference between the hedging portfolio's terminal value and the option payoff:
/// <c>FinalPortfolioValue − OptionPayoff</c>. A perfectly hedged position yields zero;
/// discrete rebalancing introduces non-zero error whose standard deviation shrinks as
/// rebalancing frequency increases.
/// </param>
public sealed record DeltaHedgeSimulationResult(
    decimal StartingOptionPrice,
    IReadOnlyList<double> Deltas,
    IReadOnlyList<decimal> Spots,
    decimal OptionPayoff,
    decimal FinalPortfolioValue,
    decimal HedgingError) : ICalculationResult
{
    /// <summary>Owned read-only delta sequence, with one value per rebalance.</summary>
    public IReadOnlyList<double> Deltas { get; init => field = Copy(value); } = Copy(Deltas);

    /// <summary>Owned read-only snapshot of the realized spot path.</summary>
    public IReadOnlyList<decimal> Spots { get; init => field = Copy(value); } = Copy(Spots);

    /// <summary>
    /// <see langword="true"/> when the simulation actually ran — i.e. the path had at
    /// least one rebalance step and therefore recorded at least one delta.
    /// </summary>
    /// <remarks>
    /// <see cref="DeltaHedgeSimulator.Simulate"/> short-circuits a degenerate path
    /// (fewer than two spots, so no step to hedge) by returning an all-zero sentinel
    /// with an empty <see cref="Deltas"/> list. That sentinel reports
    /// <see langword="false"/> so a caller cannot mistake it for a genuine zero-payoff
    /// hedge: a successful run always records one delta per rebalance, so a non-empty
    /// <see cref="Deltas"/> is the exact, derivable witness of success.
    /// </remarks>
    public bool IsSuccess => Deltas.Count > 0;

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.ToArray());
    }
}
