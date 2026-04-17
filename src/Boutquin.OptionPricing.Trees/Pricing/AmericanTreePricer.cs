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
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Domain.Results;
using Boutquin.OptionPricing.Trees.Binomial;

namespace Boutquin.OptionPricing.Trees.Pricing;

/// <summary>
/// Prices American options on a pre-built recombining binomial tree using backward induction
/// with an early exercise check at every interior node. At each node the option value is
/// the maximum of the continuation value and the immediate exercise payoff.
/// </summary>
/// <remarks>
/// <para>
/// At each backward induction step:
/// <c>V(i) = max(exercise(i), exp(-r * dt) * [p * V_up(i) + (1 - p) * V_down(i)])</c>,
/// where <c>exercise(i)</c> is the intrinsic payoff at the node's spot price.
/// </para>
/// <para>
/// The early exercise feature means American option prices are always at least as large
/// as the corresponding European prices. The exercise boundary — the set of nodes where
/// exercise dominates continuation — can be extracted separately via
/// <see cref="Binomial.ExerciseBoundaryResult"/>.
/// </para>
/// </remarks>
public sealed class AmericanTreePricer : IContextualOptionPricer<AmericanOption, OptionMarketData, BinomialTree, PriceResult>
{
    /// <summary>
    /// Computes the present value of an American option by backward induction with early
    /// exercise through the supplied binomial tree.
    /// </summary>
    /// <param name="option">American option contract specifying call/put type, strike, and expiry.</param>
    /// <param name="market">Market data snapshot providing the risk-free rate term structure.</param>
    /// <param name="tree">Pre-built recombining binomial tree with spot lattice and CRR parameters.</param>
    /// <returns>
    /// A <see cref="PriceResult"/> containing the model price (reflecting the early exercise
    /// premium), calculation status, and metadata including the number of time steps used.
    /// </returns>
    public PriceResult Price(AmericanOption option, OptionMarketData market, BinomialTree tree)
    {
        List<double> values = new(tree.TerminalSpots.Count);
        foreach (double spot in tree.TerminalSpots)
        {
            values.Add(Payoff(option, spot));
        }

        double discount = Math.Exp(-market.RiskFreeRate(option.ExpiryDate) * tree.DeltaTime);
        double p = tree.RiskNeutralProbability;

        for (int step = tree.Steps - 1; step >= 0; step--)
        {
            List<double> next = new(step + 1);
            IReadOnlyList<double> spots = tree.SpotLattice[step];

            for (int i = 0; i <= step; i++)
            {
                double continuation = discount * ((p * values[i]) + ((1d - p) * values[i + 1]));
                double exercise = Payoff(option, spots[i]);
                next.Add(Math.Max(continuation, exercise));
            }

            values = next;
        }

        return new PriceResult(
            Convert.ToDecimal(values[0]),
            null,
            CalculationStatus.Success,
            new Dictionary<string, object?> { ["steps"] = tree.Steps },
            nameof(AmericanTreePricer));
    }

    private static double Payoff(AmericanOption option, double spot)
    {
        return option.CallPut switch
        {
            CallPut.Call => Math.Max(spot - (double)option.Strike, 0d),
            CallPut.Put => Math.Max((double)option.Strike - spot, 0d),
            _ => 0d
        };
    }
}
