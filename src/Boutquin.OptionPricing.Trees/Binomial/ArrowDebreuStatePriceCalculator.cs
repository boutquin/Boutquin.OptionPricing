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
/// Computes Arrow-Debreu state prices for the terminal layer of a recombining binomial tree.
/// Each state price represents the present value of receiving one unit of currency in that
/// specific terminal state, discounted at the risk-free rate and weighted by the risk-neutral
/// path probability.
/// </summary>
/// <remarks>
/// <para>
/// For a CRR tree with N steps, the Arrow-Debreu state price at terminal node j is:
/// <c>AD(j) = exp(-r * N * dt) * C(N, j) * p^(N-j) * (1-p)^j</c>,
/// where <c>C(N, j)</c> is the binomial coefficient, <c>p</c> is the risk-neutral probability,
/// and <c>r</c> is the continuously compounded risk-free rate.
/// </para>
/// <para>
/// Arrow-Debreu state prices are foundational for exotic pricing, local volatility calibration,
/// and verifying that the tree is arbitrage-free (state prices must sum to the discount factor).
/// </para>
/// </remarks>
public sealed class ArrowDebreuStatePriceCalculator
{
    /// <summary>
    /// Computes the Arrow-Debreu state price for each terminal node of the binomial tree.
    /// </summary>
    /// <param name="tree">A fully constructed recombining binomial tree.</param>
    /// <param name="rate">
    /// Continuously compounded risk-free rate used for discounting. This must be the same
    /// rate used to build the tree so that state prices use the same discounting convention.
    /// State prices sum to <c>exp(-rate * DeltaTime * Steps)</c> for any valid probability lattice.
    /// </param>
    /// <returns>
    /// A list of Arrow-Debreu state prices, one per terminal node, indexed consistently
    /// with <see cref="BinomialTree.TerminalSpots"/> (state 0 = most up moves).
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tree"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the tree's risk-neutral probability is not strictly within (0, 1), which
    /// would make the state-price weighting ill-defined.
    /// </exception>
    public IReadOnlyList<double> CalculateTerminalStatePrices(BinomialTree tree, double rate)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (!double.IsFinite(rate))
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "The discount rate must be finite.");
        }

        double p = tree.RiskNeutralProbability;
        if (p <= 0d || p >= 1d)
        {
            throw new InvalidOperationException(
                $"Risk-neutral probability must lie strictly within (0, 1); was {p}.");
        }

        TreeValidation.Validate(tree);

        if (tree.ProbabilityLattice is not null)
        {
            // Forward induction sums all paths entering each state. A single binomial
            // coefficient is valid only for a constant transition probability.
            var weights = new double[tree.Steps + 1];
            var next = new double[tree.Steps + 1];
            weights[0] = 1d;
            for (int step = 0; step < tree.Steps; step++)
            {
                Array.Clear(next);
                for (int node = 0; node <= step; node++)
                {
                    double probability = tree.ProbabilityLattice[step][node];
                    next[node] += weights[node] * probability;
                    next[node + 1] += weights[node] * (1d - probability);
                }

                (weights, next) = (next, weights);
            }

            double discount = Math.Exp(-rate * tree.DeltaTime * tree.Steps);
            if (!double.IsFinite(discount) || discount <= 0d)
            {
                throw new ArithmeticException("The tree discount factor is outside the finite positive numeric range.");
            }

            for (int node = 0; node < weights.Length; node++)
            {
                weights[node] *= discount;
            }

            return Array.AsReadOnly(weights);
        }

        int n = tree.Steps;
        List<double> prices = new(n + 1);

        // Work in log-space to stay numerically stable for large N: the binomial
        // coefficient C(N, j) overflows double well before N = 1030, and p^(N-j)
        // underflows to 0, so the naive product is NaN/Inf. The log form is exact
        // for small N and finite for large N.
        double logDiscount = -rate * tree.DeltaTime * n;
        double logP = Math.Log(p);
        double logQ = Math.Log(1d - p);

        for (int j = 0; j <= n; j++)
        {
            double logStatePrice = logDiscount + LogBinomialCoefficient(n, j) + ((n - j) * logP) + (j * logQ);
            prices.Add(Math.Exp(logStatePrice));
        }

        return prices;
    }

    private static double LogBinomialCoefficient(int n, int k)
    {
        if (k < 0 || k > n || n == 0)
        {
            return 0d;
        }

        k = Math.Min(k, n - k);
        double logC = 0d;
        for (int i = 0; i < k; i++)
        {
            logC += Math.Log(n - i) - Math.Log(i + 1);
        }

        return logC;
    }
}
