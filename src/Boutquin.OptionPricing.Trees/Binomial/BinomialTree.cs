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

namespace Boutquin.OptionPricing.Trees.Binomial;

/// <summary>
/// Immutable representation of a fully constructed recombining binomial tree.
/// Stores the lattice of spot prices at every (time step, state) pair along with
/// the CRR parameters needed for backward induction pricing.
/// </summary>
/// <param name="Steps">Total number of time steps in the tree (N). The tree has N+1 layers, from valuation date to expiry.</param>
/// <param name="DeltaTime">Length of a single time step in years: <c>dt = T / N</c>.</param>
/// <param name="UpFactor">Multiplicative up factor applied to the spot at each time step. CRR uses <c>u = exp(sigma * sqrt(dt))</c>; other parameterizations (for example Jarrow-Rudd) use different factors.</param>
/// <param name="DownFactor">Multiplicative down factor. In a CRR tree this is the reciprocal of <paramref name="UpFactor"/> (<c>d = 1 / u</c>); in a Jarrow-Rudd tree <c>d ≠ 1 / u</c>.</param>
/// <param name="RiskNeutralProbability">Probability of an up move under the risk-neutral measure. CRR uses <c>p = (exp((r - q) * dt) - d) / (u - d)</c>; Jarrow-Rudd fixes <c>p = 0.5</c>.</param>
/// <param name="SpotLattice">Jagged list of spot prices indexed as <c>SpotLattice[timeStep][stateIndex]</c>. State 0 is the highest spot (most up moves); this ordering holds for any parameterization with <c>u &gt; d</c>.</param>
public sealed record BinomialTree(
    int Steps,
    double DeltaTime,
    double UpFactor,
    double DownFactor,
    double RiskNeutralProbability,
    IReadOnlyList<IReadOnlyList<double>> SpotLattice)
{
    /// <summary>Positive step count bounded by <see cref="TreeBuilderLimits.MaxSteps"/>.</summary>
    public int Steps { get; init => field = ValidateSteps(value); } = ValidateSteps(Steps);

    /// <summary>Finite, strictly positive length of each step in years.</summary>
    public double DeltaTime { get; init => field = PositiveFinite(value, nameof(DeltaTime)); } = PositiveFinite(DeltaTime, nameof(DeltaTime));

    /// <summary>Finite, strictly positive multiplier for an up move.</summary>
    public double UpFactor { get; init => field = PositiveFinite(value, nameof(UpFactor)); } = PositiveFinite(UpFactor, nameof(UpFactor));

    /// <summary>Finite, strictly positive multiplier for a down move.</summary>
    public double DownFactor { get; init => field = PositiveFinite(value, nameof(DownFactor)); } = PositiveFinite(DownFactor, nameof(DownFactor));

    /// <summary>Finite probability of an up move, in the closed interval [0,1].</summary>
    public double RiskNeutralProbability { get; init => field = Probability(value); } = Probability(RiskNeutralProbability);

    /// <summary>Owned, read-only triangular spot lattice; every spot is finite and positive.</summary>
    public IReadOnlyList<IReadOnlyList<double>> SpotLattice { get; init => field = TreeValidation.CopyLattice(value); } = TreeValidation.CopyLattice(SpotLattice);

    /// <summary>
    /// Continuously compounded risk-free rate used to bake
    /// <see cref="RiskNeutralProbability"/> at build time, in the same units the
    /// per-step discount factor consumes.
    /// </summary>
    /// <remarks>
    /// When set, pricers discount with this rate so that the up-move probability and
    /// the discounting share a single source — the only configuration that is
    /// internally arbitrage-consistent. When <see langword="null"/> (for example a
    /// tree constructed directly without a known build rate), pricers fall back to the
    /// rate read from the price-time market, and the caller is responsible for ensuring
    /// that market matches the one the tree was built from.
    /// </remarks>
    public double? RiskFreeRate
    {
        get;
        init
        {
            if (value.HasValue && !double.IsFinite(value.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The tree discount rate must be finite.");
            }

            field = value;
        }
    }

    /// <summary>
    /// Optional per-node up-move probability lattice, indexed
    /// <c>ProbabilityLattice[timeStep][stateIndex]</c> in the same top-down orientation as
    /// <see cref="SpotLattice"/>: entry <c>[step][j]</c> is the probability that the node at
    /// <c>SpotLattice[step][j]</c> moves up to <c>SpotLattice[step+1][j]</c> (the higher-spot
    /// child) rather than down to <c>SpotLattice[step+1][j+1]</c>. Each interior layer
    /// <c>ProbabilityLattice[step]</c> has <c>step + 1</c> entries.
    /// </summary>
    /// <remarks>
    /// Set only by smile-calibrated constructions (a Derman-Kani / Rubinstein implied tree),
    /// where each node carries a distinct martingale transition probability and those per-node
    /// weights — not their average — are what make the tree reprice the volatility surface.
    /// When <see langword="null"/> (the default; constant-volatility CRR and Jarrow-Rudd trees,
    /// which correctly have a single probability), backward-induction pricers fall back to the
    /// scalar <see cref="RiskNeutralProbability"/>. A pricer that consumes this lattice must
    /// fall back to the scalar whenever it is <see langword="null"/>.
    /// </remarks>
    public IReadOnlyList<IReadOnlyList<double>>? ProbabilityLattice
    {
        get;
        init => field = value is null ? null : TreeValidation.CopyLattice(value, probabilities: true);
    }

    /// <summary>
    /// Spot prices at the terminal (expiry) layer of the binomial tree,
    /// ordered from highest to lowest. Used as the starting point for
    /// backward induction when computing option values.
    /// </summary>
    public IReadOnlyList<double> TerminalSpots => SpotLattice[^1];

    private static int ValidateSteps(int value)
    {
        if (value < 1 || value > TreeBuilderLimits.MaxSteps)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Tree steps must be within the supported positive range.");
        }

        return value;
    }

    private static double PositiveFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0d)
        {
            throw new ArgumentOutOfRangeException(name, "Value must be finite and positive.");
        }

        return value;
    }

    private static double Probability(double value)
    {
        if (!double.IsFinite(value) || value < 0d || value > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "An up probability must be finite and within [0,1].");
        }

        return value;
    }
}
