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

using Boutquin.Domain.Exceptions;
using Boutquin.Domain.Extensions;
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
/// exercise dominates continuation — can be extracted via
/// <see cref="ComputeExerciseBoundary"/>, which returns an
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
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="option"/>, <paramref name="market"/>, or
    /// <paramref name="tree"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="CurrencyMismatchException">
    /// Thrown when <paramref name="market"/>'s spot currency differs from
    /// <paramref name="option"/>'s contract currency.
    /// </exception>
    public PriceResult Price(AmericanOption option, OptionMarketData market, BinomialTree tree)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(tree);
        TreeValidation.Validate(tree);

        if (market.Spot.Currency != option.Currency)
        {
            throw new CurrencyMismatchException(option.Currency, market.Spot.Currency);
        }

        List<double> values = new(tree.TerminalSpots.Count);
        foreach (double spot in tree.TerminalSpots)
        {
            values.Add(Payoff(option, spot));
        }

        double rate = tree.RiskFreeRate ?? market.RiskFreeRate(option.ExpiryDate);
        double discount = Math.Exp(-rate * tree.DeltaTime);
        double pScalar = tree.RiskNeutralProbability;
        IReadOnlyList<IReadOnlyList<double>>? pLattice = tree.ProbabilityLattice;

        for (int step = tree.Steps - 1; step >= 0; step--)
        {
            List<double> next = new(step + 1);
            IReadOnlyList<double> spots = tree.SpotLattice[step];
            IReadOnlyList<double>? stepProbs = pLattice?[step];

            for (int i = 0; i <= step; i++)
            {
                double p = stepProbs is null ? pScalar : stepProbs[i];
                double continuation = discount * ((p * values[i]) + ((1d - p) * values[i + 1]));
                double exercise = Payoff(option, spots[i]);
                next.Add(Math.Max(continuation, exercise));
            }

            values = next;
        }

        if (!double.IsFinite(values[0]) || values[0] < 0d || values[0] >= (double)decimal.MaxValue)
        {
            return PriceResult.Unsupported(nameof(AmericanTreePricer), "The tree valuation exceeded the supported finite decimal price range.", option.Currency);
        }

        return new PriceResult(
            option.Currency.Amount(Convert.ToDecimal(values[0])),
            null,
            CalculationStatus.Success,
            new Dictionary<string, object?> { ["steps"] = tree.Steps },
            nameof(AmericanTreePricer));
    }

    /// <summary>
    /// Extracts the early-exercise boundary from an American backward-induction pass.
    /// For each interior time step where at least one node's immediate exercise value
    /// strictly exceeds its continuation value, records the lowest state index (highest
    /// spot, since index 0 is the highest spot) at which exercise dominates.
    /// </summary>
    /// <param name="option">American option contract specifying call/put type, strike, and expiry.</param>
    /// <param name="market">Market data snapshot providing the risk-free rate term structure.</param>
    /// <param name="tree">Pre-built recombining binomial tree with spot lattice and CRR parameters.</param>
    /// <returns>
    /// An <see cref="ExerciseBoundaryResult"/> whose boundaries are ordered by ascending
    /// time step. The result is unsuccessful (empty) when early exercise is never optimal
    /// (for example an American call on a non-dividend-paying underlier).
    /// </returns>
    /// <exception cref="CurrencyMismatchException">
    /// Thrown when <paramref name="market"/>'s spot currency differs from <paramref name="option"/>'s
    /// contract currency.
    /// </exception>
    public ExerciseBoundaryResult ComputeExerciseBoundary(AmericanOption option, OptionMarketData market, BinomialTree tree)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(tree);
        TreeValidation.Validate(tree);

        if (market.Spot.Currency != option.Currency)
        {
            throw new CurrencyMismatchException(option.Currency, market.Spot.Currency);
        }

        List<double> values = new(tree.TerminalSpots.Count);
        foreach (double spot in tree.TerminalSpots)
        {
            values.Add(Payoff(option, spot));
        }

        double rate = tree.RiskFreeRate ?? market.RiskFreeRate(option.ExpiryDate);
        double discount = Math.Exp(-rate * tree.DeltaTime);
        double pScalar = tree.RiskNeutralProbability;
        IReadOnlyList<IReadOnlyList<double>>? pLattice = tree.ProbabilityLattice;
        List<(int Step, int LowestExerciseIndex)> boundaries = new();

        for (int step = tree.Steps - 1; step >= 0; step--)
        {
            List<double> next = new(step + 1);
            IReadOnlyList<double> spots = tree.SpotLattice[step];
            IReadOnlyList<double>? stepProbs = pLattice?[step];
            int lowestExerciseIndex = -1;

            for (int i = 0; i <= step; i++)
            {
                double p = stepProbs is null ? pScalar : stepProbs[i];
                double continuation = discount * ((p * values[i]) + ((1d - p) * values[i + 1]));
                double exercise = Payoff(option, spots[i]);
                if (exercise > continuation && lowestExerciseIndex < 0)
                {
                    lowestExerciseIndex = i;
                }

                next.Add(Math.Max(continuation, exercise));
            }

            if (lowestExerciseIndex >= 0)
            {
                boundaries.Add((step, lowestExerciseIndex));
            }

            values = next;
        }

        if (!double.IsFinite(values[0]))
        {
            throw new ArithmeticException("The exercise-boundary valuation exceeded the finite numeric range.");
        }

        boundaries.Reverse();
        return new ExerciseBoundaryResult(boundaries);
    }

    /// <summary>
    /// Prices the option by backward induction with early exercise and, additionally, retains the
    /// full option-value lattice (every interior layer, not just the root) so a downstream consumer
    /// can extract lattice-derived sensitivities without re-pricing.
    /// </summary>
    /// <param name="option">American option contract specifying call/put type, strike, and expiry.</param>
    /// <param name="market">Market data snapshot providing the risk-free rate term structure.</param>
    /// <param name="tree">Pre-built recombining binomial tree with spot lattice and CRR parameters.</param>
    /// <returns>
    /// A <see cref="LatticeValuationResult"/> pairing the supplied <paramref name="tree"/> with the
    /// computed value lattice (each node holding <c>max(continuation, exercise)</c>).
    /// <see cref="LatticeValuationResult.RootValue"/> equals the price returned by
    /// <see cref="Price(AmericanOption, OptionMarketData, BinomialTree)"/> for the same inputs.
    /// </returns>
    /// <remarks>
    /// This is an additive overload: it reuses the identical backward-induction-with-early-exercise
    /// recursion as <see cref="Price(AmericanOption, OptionMarketData, BinomialTree)"/>, differing only
    /// in that it records each layer of values. Intended primary consumer:
    /// <see cref="TreeGreeksCalculator"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="option"/>, <paramref name="market"/>, or
    /// <paramref name="tree"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="CurrencyMismatchException">
    /// Thrown when <paramref name="market"/>'s spot currency differs from
    /// <paramref name="option"/>'s contract currency.
    /// </exception>
    /// <exception cref="ArithmeticException">The valued lattice cannot be represented in the supported finite decimal price range.</exception>
    public LatticeValuationResult PriceWithLattice(AmericanOption option, OptionMarketData market, BinomialTree tree)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(tree);
        TreeValidation.Validate(tree);

        if (market.Spot.Currency != option.Currency)
        {
            throw new CurrencyMismatchException(option.Currency, market.Spot.Currency);
        }

        var valueLattice = new IReadOnlyList<double>[tree.Steps + 1];

        List<double> values = new(tree.TerminalSpots.Count);
        foreach (double spot in tree.TerminalSpots)
        {
            values.Add(Payoff(option, spot));
        }

        valueLattice[tree.Steps] = values;

        double rate = tree.RiskFreeRate ?? market.RiskFreeRate(option.ExpiryDate);
        double discount = Math.Exp(-rate * tree.DeltaTime);
        double pScalar = tree.RiskNeutralProbability;
        IReadOnlyList<IReadOnlyList<double>>? pLattice = tree.ProbabilityLattice;

        for (int step = tree.Steps - 1; step >= 0; step--)
        {
            List<double> next = new(step + 1);
            IReadOnlyList<double> spots = tree.SpotLattice[step];
            IReadOnlyList<double>? stepProbs = pLattice?[step];

            for (int i = 0; i <= step; i++)
            {
                double p = stepProbs is null ? pScalar : stepProbs[i];
                double continuation = discount * ((p * values[i]) + ((1d - p) * values[i + 1]));
                double exercise = Payoff(option, spots[i]);
                next.Add(Math.Max(continuation, exercise));
            }

            valueLattice[step] = next;
            values = next;
        }

        if (!double.IsFinite(values[0]) || values[0] < 0d || values[0] >= (double)decimal.MaxValue)
        {
            throw new ArithmeticException("The tree valuation exceeded the supported finite decimal price range.");
        }

        return new LatticeValuationResult(tree, valueLattice);
    }

    private static double Payoff(AmericanOption option, double spot)
    {
        return option.CallPut switch
        {
            CallPut.Call => Math.Max(spot - (double)option.Strike.Amount, 0d),
            CallPut.Put => Math.Max((double)option.Strike.Amount - spot, 0d),
            _ => throw new NotSupportedException($"Unsupported call/put flag: {option.CallPut}.")
        };
    }
}
