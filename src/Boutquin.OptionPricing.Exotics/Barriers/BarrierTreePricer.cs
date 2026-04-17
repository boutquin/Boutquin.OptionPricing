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

namespace Boutquin.OptionPricing.Exotics.Barriers;

/// <summary>
/// Prices barrier options on a pre-built binomial tree using backward induction with
/// barrier monitoring at each node. Knock-out options are priced directly by zeroing
/// node values where the barrier has been breached. Knock-in options are derived via
/// barrier parity: V_in = V_vanilla − V_out.
/// </summary>
/// <remarks>
/// <para>
/// At each node during backward induction, the pricer checks whether the underlying
/// spot has breached the barrier. For knock-out options, breached nodes receive zero
/// continuation value. The barrier breach check uses:
/// </para>
/// <list type="bullet">
/// <item>Up barrier: <c>spot &gt;= barrierLevel</c></item>
/// <item>Down barrier: <c>spot &lt;= barrierLevel</c></item>
/// </list>
/// <para>
/// For knock-in options, the complementary knock-out is computed first, then barrier
/// parity is applied: V_knockIn = V_vanilla − V_knockOut.
/// </para>
/// </remarks>
public sealed class BarrierTreePricer : IContextualOptionPricer<BarrierOption, OptionMarketData, BinomialTree, PriceResult>
{
    /// <summary>
    /// Prices a barrier option on the supplied binomial tree.
    /// </summary>
    /// <param name="option">The barrier option contract specifying strike, barrier level, direction, and knock type.</param>
    /// <param name="market">The market data snapshot providing spot, volatility, and curves.</param>
    /// <param name="context">The pre-built binomial tree to traverse.</param>
    /// <returns>
    /// A <see cref="PriceResult"/> with the barrier option price. For knock-out options the
    /// price is computed directly; for knock-in options it is derived via barrier parity.
    /// </returns>
    public PriceResult Price(BarrierOption option, OptionMarketData market, BinomialTree context)
    {
        // Check if barrier is already breached at spot (root node)
        double spot0 = context.SpotLattice[0][0];
        if (IsBarrierBreached(spot0, option))
        {
            if (option.KnockType == BarrierKnockType.Out)
            {
                return new PriceResult(0m, null, CalculationStatus.Success,
                    new Dictionary<string, object?> { ["barrierBreachedAtSpot"] = true },
                    nameof(BarrierTreePricer));
            }

            // Knock-in already activated at spot → option is vanilla from here
            decimal vanillaPrice = PriceVanilla(option, market, context);
            return new PriceResult(vanillaPrice, null, CalculationStatus.Success,
                new Dictionary<string, object?> { ["barrierBreachedAtSpot"] = true, ["knockInActivated"] = true },
                nameof(BarrierTreePricer));
        }

        if (option.KnockType == BarrierKnockType.Out)
        {
            decimal knockOutPrice = PriceKnockOut(option, market, context);
            return new PriceResult(knockOutPrice, null, CalculationStatus.Success,
                new Dictionary<string, object?> { ["steps"] = context.Steps },
                nameof(BarrierTreePricer));
        }

        // Knock-in via parity: V_in = V_vanilla - V_out
        decimal vanilla = PriceVanilla(option, market, context);
        decimal knockOut = PriceKnockOut(option, market, context);
        decimal knockIn = vanilla - knockOut;
        return new PriceResult(Math.Max(knockIn, 0m), null, CalculationStatus.Success,
            new Dictionary<string, object?>
            {
                ["steps"] = context.Steps,
                ["vanillaPrice"] = vanilla,
                ["knockOutPrice"] = knockOut
            },
            nameof(BarrierTreePricer));
    }

    private static decimal PriceKnockOut(BarrierOption option, OptionMarketData market, BinomialTree tree)
    {
        double discount = Math.Exp(-market.RiskFreeRate(option.ExpiryDate) * tree.DeltaTime);
        double p = tree.RiskNeutralProbability;

        // Terminal payoffs with barrier check
        List<double> values = new(tree.TerminalSpots.Count);
        foreach (double spot in tree.TerminalSpots)
        {
            if (IsBarrierBreached(spot, option))
            {
                values.Add(0d);
            }
            else
            {
                values.Add(Payoff(option, spot));
            }
        }

        // Backward induction with barrier monitoring at each node
        for (int step = tree.Steps - 1; step >= 0; step--)
        {
            List<double> next = new(step + 1);
            for (int j = 0; j <= step; j++)
            {
                double nodeSpot = tree.SpotLattice[step][j];
                if (IsBarrierBreached(nodeSpot, option))
                {
                    next.Add(0d);
                }
                else
                {
                    double continuation = discount * ((p * values[j]) + ((1d - p) * values[j + 1]));
                    next.Add(continuation);
                }
            }

            values = next;
        }

        return Convert.ToDecimal(Math.Max(values[0], 0d));
    }

    private static decimal PriceVanilla(BarrierOption option, OptionMarketData market, BinomialTree tree)
    {
        double discount = Math.Exp(-market.RiskFreeRate(option.ExpiryDate) * tree.DeltaTime);
        double p = tree.RiskNeutralProbability;

        List<double> values = new(tree.TerminalSpots.Count);
        foreach (double spot in tree.TerminalSpots)
        {
            values.Add(Payoff(option, spot));
        }

        for (int step = tree.Steps - 1; step >= 0; step--)
        {
            List<double> next = new(step + 1);
            for (int j = 0; j <= step; j++)
            {
                double continuation = discount * ((p * values[j]) + ((1d - p) * values[j + 1]));
                next.Add(continuation);
            }

            values = next;
        }

        return Convert.ToDecimal(values[0]);
    }

    private static bool IsBarrierBreached(double spot, BarrierOption option)
    {
        return option.Direction switch
        {
            BarrierDirection.Up => spot >= (double)option.BarrierLevel,
            BarrierDirection.Down => spot <= (double)option.BarrierLevel,
            _ => false
        };
    }

    private static double Payoff(BarrierOption option, double spot)
    {
        return option.CallPut switch
        {
            CallPut.Call => Math.Max(spot - (double)option.Strike, 0d),
            CallPut.Put => Math.Max((double)option.Strike - spot, 0d),
            _ => 0d
        };
    }
}
