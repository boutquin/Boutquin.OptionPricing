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

using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Trees.Binomial;

namespace Boutquin.OptionPricing.Exotics.Barriers;

/// <summary>
/// Shared backward-induction kernels for barrier pricing on a recombining binomial tree.
/// Both <see cref="BarrierTreePricer"/> (flat-vol CRR tree) and
/// <see cref="SmileAwareBarrierPricer"/> (calibrated implied tree) delegate here so the
/// barrier-monitoring, vanilla, payoff, and breach logic exists in exactly one place.
/// </summary>
internal static class BarrierBackwardInduction
{
    /// <summary>
    /// Prices the knock-out leg directly: terminal and interior nodes whose spot has
    /// breached the barrier receive zero value; all other nodes roll back normally.
    /// </summary>
    public static decimal PriceKnockOut(BarrierOption option, OptionMarketData market, BinomialTree tree)
    {
        double rate = tree.RiskFreeRate ?? market.RiskFreeRate(option.ExpiryDate);
        double discount = Math.Exp(-rate * tree.DeltaTime);
        double pScalar = tree.RiskNeutralProbability;
        IReadOnlyList<IReadOnlyList<double>>? pLattice = tree.ProbabilityLattice;

        List<double> values = new(tree.TerminalSpots.Count);
        foreach (double spot in tree.TerminalSpots)
        {
            values.Add(IsBarrierBreached(spot, option) ? 0d : Payoff(option, spot));
        }

        for (int step = tree.Steps - 1; step >= 0; step--)
        {
            List<double> next = new(step + 1);
            IReadOnlyList<double> spots = tree.SpotLattice[step];
            IReadOnlyList<double>? stepProbs = pLattice?[step];
            for (int j = 0; j <= step; j++)
            {
                if (IsBarrierBreached(spots[j], option))
                {
                    next.Add(0d);
                }
                else
                {
                    double p = stepProbs is null ? pScalar : stepProbs[j];
                    double continuation = discount * ((p * values[j]) + ((1d - p) * values[j + 1]));
                    next.Add(continuation);
                }
            }

            values = next;
        }

        return RepresentablePrice(Math.Max(values[0], 0d));
    }

    /// <summary>
    /// Prices the vanilla (no-barrier) option on the same tree, used as the
    /// <c>V_in = V_vanilla − V_out</c> reference for knock-in pricing via barrier parity.
    /// </summary>
    public static decimal PriceVanilla(BarrierOption option, OptionMarketData market, BinomialTree tree)
    {
        double rate = tree.RiskFreeRate ?? market.RiskFreeRate(option.ExpiryDate);
        double discount = Math.Exp(-rate * tree.DeltaTime);
        double pScalar = tree.RiskNeutralProbability;
        IReadOnlyList<IReadOnlyList<double>>? pLattice = tree.ProbabilityLattice;

        List<double> values = new(tree.TerminalSpots.Count);
        foreach (double spot in tree.TerminalSpots)
        {
            values.Add(Payoff(option, spot));
        }

        for (int step = tree.Steps - 1; step >= 0; step--)
        {
            List<double> next = new(step + 1);
            IReadOnlyList<double>? stepProbs = pLattice?[step];
            for (int j = 0; j <= step; j++)
            {
                double p = stepProbs is null ? pScalar : stepProbs[j];
                double continuation = discount * ((p * values[j]) + ((1d - p) * values[j + 1]));
                next.Add(continuation);
            }

            values = next;
        }

        return RepresentablePrice(values[0]);
    }

    /// <summary>
    /// Returns whether the spot has breached the barrier (up: spot ≥ level; down: spot ≤ level).
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown for an unrecognized <see cref="BarrierDirection"/>.</exception>
    public static bool IsBarrierBreached(double spot, BarrierOption option)
    {
        return option.Direction switch
        {
            BarrierDirection.Up => spot >= (double)option.BarrierLevel,
            BarrierDirection.Down => spot <= (double)option.BarrierLevel,
            _ => throw new NotSupportedException($"Unsupported barrier direction: {option.Direction}.")
        };
    }

    private static decimal RepresentablePrice(double value)
    {
        try
        {
            return Convert.ToDecimal(value);
        }
        catch (OverflowException ex)
        {
            throw new InvalidOperationException("The barrier lattice produced a price outside the finite decimal range.", ex);
        }
    }

    private static double Payoff(BarrierOption option, double spot)
    {
        return option.CallPut switch
        {
            CallPut.Call => Math.Max(spot - (double)option.Strike.Amount, 0d),
            CallPut.Put => Math.Max((double)option.Strike.Amount - spot, 0d),
            _ => throw new NotSupportedException($"Unsupported call/put flag: {option.CallPut}.")
        };
    }
}
