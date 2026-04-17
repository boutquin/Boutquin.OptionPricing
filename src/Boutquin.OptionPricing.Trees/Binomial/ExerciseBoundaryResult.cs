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

namespace Boutquin.OptionPricing.Trees.Binomial;

/// <summary>
/// Captures the early exercise boundary extracted from an American option pricing pass
/// on a binomial tree. At each time step where early exercise is optimal for at least
/// one node, the boundary records the lowest state index at which exercise dominates
/// continuation value.
/// </summary>
/// <param name="Boundaries">
/// Ordered list of (time step, lowest exercise state index) pairs. Only time steps
/// where at least one node exercises optimally are included. The state index
/// references <see cref="BinomialTree.SpotLattice"/> ordering (0 = highest spot).
/// </param>
public sealed record ExerciseBoundaryResult(
    IReadOnlyList<(int Step, int LowestExerciseIndex)> Boundaries) : ICalculationResult
{
    /// <inheritdoc />
    public bool IsSuccess => Boundaries.Count > 0;
}
