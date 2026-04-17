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
    /// <param name="rate">Continuously compounded risk-free rate used for discounting.</param>
    /// <returns>
    /// A list of Arrow-Debreu state prices, one per terminal node, indexed consistently
    /// with <see cref="BinomialTree.TerminalSpots"/> (state 0 = most up moves).
    /// </returns>
    public IReadOnlyList<double> CalculateTerminalStatePrices(BinomialTree tree, double rate)
    {
        List<double> prices = new(tree.Steps + 1);
        double discount = Math.Exp(-rate * tree.DeltaTime * tree.Steps);
        double p = tree.RiskNeutralProbability;

        for (int j = 0; j <= tree.Steps; j++)
        {
            double combination = BinomialCoefficient(tree.Steps, j);
            double statePrice = discount * combination * Math.Pow(p, tree.Steps - j) * Math.Pow(1d - p, j);
            prices.Add(statePrice);
        }

        return prices;
    }

    private static double BinomialCoefficient(int n, int k)
    {
        if (k < 0 || k > n)
        {
            return 0d;
        }

        if (k == 0 || k == n)
        {
            return 1d;
        }

        k = Math.Min(k, n - k);
        double c = 1d;
        for (int i = 0; i < k; i++)
        {
            c *= (n - i);
            c /= (i + 1);
        }

        return c;
    }
}
