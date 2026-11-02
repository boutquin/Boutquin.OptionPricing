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

internal static class TreeValidation
{
    internal static IReadOnlyList<IReadOnlyList<double>> CopyLattice(IReadOnlyList<IReadOnlyList<double>> lattice, bool probabilities = false, bool values = false)
    {
        ArgumentNullException.ThrowIfNull(lattice);
        if (lattice.Count < 1 || lattice.Count > TreeBuilderLimits.MaxSteps + 1)
        {
            throw new ArgumentException("Lattice layer count is outside the supported range.", nameof(lattice));
        }

        var copy = new IReadOnlyList<double>[lattice.Count];
        for (int step = 0; step < lattice.Count; step++)
        {
            var row = lattice[step];
            if (row is null || row.Count != step + 1)
            {
                throw new ArgumentException("Each lattice layer must contain step + 1 nodes.", nameof(lattice));
            }

            var nodes = new double[row.Count];
            for (int i = 0; i < nodes.Length; i++)
            {
                double node = row[i];
                if (!double.IsFinite(node) || (probabilities ? node < 0d || node > 1d : values ? node < 0d : node <= 0d))
                {
                    throw new ArgumentException("Lattice nodes must be finite and satisfy the spot, value or probability range.", nameof(lattice));
                }

                nodes[i] = node;
            }

            copy[step] = Array.AsReadOnly(nodes);
        }

        return Array.AsReadOnly(copy);
    }

    internal static void Validate(BinomialTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (tree.Steps < 1 || tree.Steps > TreeBuilderLimits.MaxSteps || tree.SpotLattice.Count != tree.Steps + 1
            || !double.IsFinite(tree.DeltaTime) || tree.DeltaTime <= 0d
            || !double.IsFinite(tree.UpFactor) || !double.IsFinite(tree.DownFactor) || tree.DownFactor <= 0d || tree.UpFactor <= tree.DownFactor
            || !double.IsFinite(tree.RiskNeutralProbability) || tree.RiskNeutralProbability < 0d || tree.RiskNeutralProbability > 1d
            || (tree.RiskFreeRate is double rate && !double.IsFinite(rate))
            || (tree.ProbabilityLattice is not null && tree.ProbabilityLattice.Count != tree.Steps))
        {
            throw new ArgumentException("The tree must have consistent finite geometry, rates and probabilities.", nameof(tree));
        }
    }
}
