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
using Boutquin.OptionPricing.Calibration.ImpliedTrees;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Domain.Results;
using Boutquin.OptionPricing.Trees.Binomial;

namespace Boutquin.OptionPricing.Exotics.Barriers;

/// <summary>
/// Prices barrier options using a calibrated implied tree that accounts for the
/// volatility smile. The calibrated tree's spot lattice already reflects smile-consistent
/// local volatilities, correcting for smile effects that flat-volatility barrier
/// tree pricing ignores.
/// </summary>
/// <remarks>
/// <para>
/// Uses the same backward induction with barrier monitoring as <see cref="BarrierTreePricer"/>,
/// but operates on the calibrated implied tree from <see cref="ImpliedTreeResult"/> rather
/// than a flat-vol CRR tree. For knock-in options, barrier parity is applied:
/// V_in = V_vanilla − V_out.
/// </para>
/// </remarks>
public sealed class SmileAwareBarrierPricer : IContextualOptionPricer<BarrierOption, OptionMarketData, ImpliedTreeResult, PriceResult>
{
    /// <summary>
    /// Prices a barrier option using a calibrated smile-aware implied tree.
    /// </summary>
    /// <param name="option">The barrier option contract.</param>
    /// <param name="market">The market data snapshot.</param>
    /// <param name="calibration">
    /// The calibrated implied tree result containing the smile-consistent
    /// spot lattice, local volatilities, and Arrow-Debreu state prices.
    /// </param>
    /// <returns>
    /// A <see cref="PriceResult"/> with the smile-aware barrier option price.
    /// </returns>
    public PriceResult Price(BarrierOption option, OptionMarketData market, ImpliedTreeResult calibration)
    {
        if (!calibration.IsSuccess)
        {
            return PriceResult.Unsupported(
                nameof(SmileAwareBarrierPricer),
                "Cannot price: calibration did not succeed.");
        }

        BinomialTree tree = calibration.Tree;

        // Check if barrier is already breached at spot
        double spot0 = tree.SpotLattice[0][0];
        if (IsBarrierBreached(spot0, option))
        {
            if (option.KnockType == BarrierKnockType.Out)
            {
                return new PriceResult(0m, null, CalculationStatus.Success,
                    new Dictionary<string, object?> { ["barrierBreachedAtSpot"] = true },
                    nameof(SmileAwareBarrierPricer));
            }

            decimal vanillaPrice = PriceVanilla(option, market, tree);
            return new PriceResult(vanillaPrice, null, CalculationStatus.Success,
                new Dictionary<string, object?> { ["barrierBreachedAtSpot"] = true },
                nameof(SmileAwareBarrierPricer));
        }

        if (option.KnockType == BarrierKnockType.Out)
        {
            decimal knockOutPrice = PriceKnockOut(option, market, tree);
            return new PriceResult(knockOutPrice, null, CalculationStatus.Success,
                new Dictionary<string, object?> { ["steps"] = tree.Steps, ["model"] = "smile-aware" },
                nameof(SmileAwareBarrierPricer));
        }

        // Knock-in via parity
        decimal vanilla = PriceVanilla(option, market, tree);
        decimal knockOut = PriceKnockOut(option, market, tree);
        decimal knockIn = Math.Max(vanilla - knockOut, 0m);
        return new PriceResult(knockIn, null, CalculationStatus.Success,
            new Dictionary<string, object?>
            {
                ["steps"] = tree.Steps,
                ["model"] = "smile-aware",
                ["vanillaPrice"] = vanilla,
                ["knockOutPrice"] = knockOut
            },
            nameof(SmileAwareBarrierPricer));
    }

    private static decimal PriceKnockOut(BarrierOption option, OptionMarketData market, BinomialTree tree)
    {
        double discount = Math.Exp(-market.RiskFreeRate(option.ExpiryDate) * tree.DeltaTime);
        double p = tree.RiskNeutralProbability;

        List<double> values = new(tree.TerminalSpots.Count);
        foreach (double spot in tree.TerminalSpots)
        {
            values.Add(IsBarrierBreached(spot, option) ? 0d : Payoff(option, spot));
        }

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
