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
    /// metadata including the number of time steps used. A root value outside the supported
    /// finite decimal range returns an unsupported-scenario result.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="option"/>, <paramref name="market"/>, or
    /// <paramref name="tree"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="CurrencyMismatchException">
    /// Thrown when <paramref name="market"/>'s spot currency differs from
    /// <paramref name="option"/>'s contract currency.
    /// </exception>
    public PriceResult Price(EuropeanOption option, OptionMarketData market, BinomialTree tree)
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
            IReadOnlyList<double>? stepProbs = pLattice?[step];
            for (int i = 0; i <= step; i++)
            {
                double p = stepProbs is null ? pScalar : stepProbs[i];
                double continuation = discount * ((p * values[i]) + ((1d - p) * values[i + 1]));
                next.Add(continuation);
            }

            values = next;
        }

        if (!double.IsFinite(values[0]) || values[0] < 0d || values[0] >= (double)decimal.MaxValue)
        {
            return PriceResult.Unsupported(nameof(EuropeanTreePricer), "The tree valuation exceeded the supported finite decimal price range.", option.Currency);
        }

        return new PriceResult(
            option.Currency.Amount(Convert.ToDecimal(values[0])),
            null,
            CalculationStatus.Success,
            new Dictionary<string, object?> { ["steps"] = tree.Steps },
            nameof(EuropeanTreePricer));
    }

    /// <summary>
    /// Prices the option by backward induction and, additionally, retains the full option-value
    /// lattice (every interior layer, not just the root) so a downstream consumer can extract
    /// lattice-derived sensitivities without re-pricing.
    /// </summary>
    /// <param name="option">European option contract specifying call/put type, strike, and expiry.</param>
    /// <param name="market">Market data snapshot providing the risk-free rate term structure.</param>
    /// <param name="tree">Pre-built recombining binomial tree with spot lattice and CRR parameters.</param>
    /// <returns>
    /// A <see cref="LatticeValuationResult"/> pairing the supplied <paramref name="tree"/> with the
    /// computed value lattice. <see cref="LatticeValuationResult.RootValue"/> equals the price returned by
    /// <see cref="Price(EuropeanOption, OptionMarketData, BinomialTree)"/> for the same inputs.
    /// </returns>
    /// <remarks>
    /// This is an additive overload: it reuses the identical risk-neutral backward-induction recursion
    /// as <see cref="Price(EuropeanOption, OptionMarketData, BinomialTree)"/>, differing only in that it
    /// records each layer of values rather than overwriting them. Intended primary consumer:
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
    public LatticeValuationResult PriceWithLattice(EuropeanOption option, OptionMarketData market, BinomialTree tree)
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
            IReadOnlyList<double>? stepProbs = pLattice?[step];
            for (int i = 0; i <= step; i++)
            {
                double p = stepProbs is null ? pScalar : stepProbs[i];
                double continuation = discount * ((p * values[i]) + ((1d - p) * values[i + 1]));
                next.Add(continuation);
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

    private static double Payoff(EuropeanOption option, double spot)
    {
        return option.CallPut switch
        {
            CallPut.Call => Math.Max(spot - (double)option.Strike.Amount, 0d),
            CallPut.Put => Math.Max((double)option.Strike.Amount - spot, 0d),
            _ => throw new NotSupportedException($"Unsupported call/put flag: {option.CallPut}.")
        };
    }
}
