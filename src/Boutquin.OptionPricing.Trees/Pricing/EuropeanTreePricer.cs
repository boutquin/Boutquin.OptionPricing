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
/// Prices European options on a pre-built recombining binomial tree using backward induction.
/// Terminal payoffs are discounted step-by-step from expiry to valuation date using the
/// risk-neutral probability; no early exercise check is performed.
/// </summary>
/// <remarks>
/// At each backward induction step the continuation value is:
/// <c>V(i) = exp(-r * dt) * [p * V_up(i) + (1 - p) * V_down(i)]</c>,
/// where <c>p</c> is the risk-neutral probability stored in the <see cref="BinomialTree"/>.
/// The price converges to Black-Scholes as the number of time steps increases.
/// </remarks>
public sealed class EuropeanTreePricer : IContextualOptionPricer<EuropeanOption, OptionMarketData, BinomialTree, PriceResult>
{
    /// <summary>
    /// Computes the present value of a European option by backward induction through
    /// the supplied binomial tree.
    /// </summary>
    /// <param name="option">European option contract specifying call/put type, strike, and expiry.</param>
    /// <param name="market">Market data snapshot providing the risk-free rate term structure.</param>
    /// <param name="tree">Pre-built recombining binomial tree with spot lattice and CRR parameters.</param>
    /// <returns>
    /// A <see cref="PriceResult"/> containing the model price, calculation status, and
    /// metadata including the number of time steps used.
    /// </returns>
    public PriceResult Price(EuropeanOption option, OptionMarketData market, BinomialTree tree)
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
            for (int i = 0; i <= step; i++)
            {
                double continuation = discount * ((p * values[i]) + ((1d - p) * values[i + 1]));
                next.Add(continuation);
            }

            values = next;
        }

        return new PriceResult(
            Convert.ToDecimal(values[0]),
            null,
            CalculationStatus.Success,
            new Dictionary<string, object?> { ["steps"] = tree.Steps },
            nameof(EuropeanTreePricer));
    }

    private static double Payoff(EuropeanOption option, double spot)
    {
        return option.CallPut switch
        {
            CallPut.Call => Math.Max(spot - (double)option.Strike, 0d),
            CallPut.Put => Math.Max((double)option.Strike - spot, 0d),
            _ => 0d
        };
    }
}
