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
/// <param name="UpFactor">Multiplicative up factor applied to the spot at each time step (CRR: <c>u = exp(sigma * sqrt(dt))</c>).</param>
/// <param name="DownFactor">Multiplicative down factor, the reciprocal of <paramref name="UpFactor"/> in a CRR tree: <c>d = 1 / u</c>.</param>
/// <param name="RiskNeutralProbability">Probability of an up move under the risk-neutral measure: <c>p = (exp((r - q) * dt) - d) / (u - d)</c>.</param>
/// <param name="SpotLattice">Jagged list of spot prices indexed as <c>SpotLattice[timeStep][stateIndex]</c>. State 0 is the highest spot (most up moves).</param>
public sealed record BinomialTree(
    int Steps,
    double DeltaTime,
    double UpFactor,
    double DownFactor,
    double RiskNeutralProbability,
    IReadOnlyList<IReadOnlyList<double>> SpotLattice)
{
    /// <summary>
    /// Spot prices at the terminal (expiry) layer of the binomial tree,
    /// ordered from highest to lowest. Used as the starting point for
    /// backward induction when computing option values.
    /// </summary>
    public IReadOnlyList<double> TerminalSpots => SpotLattice[^1];
}
