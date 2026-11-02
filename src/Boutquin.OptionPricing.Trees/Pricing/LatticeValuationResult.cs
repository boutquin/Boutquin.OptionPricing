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
using Boutquin.OptionPricing.Trees.Binomial;

namespace Boutquin.OptionPricing.Trees.Pricing;

/// <summary>
/// A fully valued binomial lattice: the source <see cref="BinomialTree"/> (spot lattice and
/// step geometry) paired with the backward-induction option-value lattice computed on it.
/// </summary>
/// <remarks>
/// <para>
/// The standard <see cref="EuropeanTreePricer.Price(Domain.Contracts.EuropeanOption, Domain.Market.OptionMarketData, BinomialTree)"/>
/// and <see cref="AmericanTreePricer.Price(Domain.Contracts.AmericanOption, Domain.Market.OptionMarketData, BinomialTree)"/>
/// passes collapse the value lattice as they roll backward, returning only the root price. The
/// lattice-returning <c>PriceWithLattice</c> overloads on those pricers retain every layer so a
/// downstream consumer — notably <see cref="TreeGreeksCalculator"/> — can read the option values
/// at the first few steps without re-pricing.
/// </para>
/// <para>
/// <see cref="ValueLattice"/> mirrors the orientation of <see cref="BinomialTree.SpotLattice"/>:
/// <c>ValueLattice[step][stateIndex]</c> is the option value at the node whose spot price is
/// <c>Tree.SpotLattice[step][stateIndex]</c>, with state 0 the highest spot (most up moves). The
/// lattice has <c>Tree.Steps + 1</c> layers; layer <c>step</c> holds <c>step + 1</c> entries.
/// </para>
/// </remarks>
/// <param name="Tree">The pre-built recombining binomial tree the values were computed on.</param>
/// <param name="ValueLattice">
/// Jagged option-value lattice indexed <c>ValueLattice[step][stateIndex]</c>, aligned to
/// <see cref="BinomialTree.SpotLattice"/>. Layer <c>0</c> holds the single root value (the model price).
/// </param>
public sealed record LatticeValuationResult(
    BinomialTree Tree,
    IReadOnlyList<IReadOnlyList<double>> ValueLattice) : ICalculationResult
{
    /// <summary>Owned triangular option-value lattice containing finite, non-negative values.</summary>
    public IReadOnlyList<IReadOnlyList<double>> ValueLattice { get; init => field = TreeValidation.CopyLattice(value, values: true); } = TreeValidation.CopyLattice(ValueLattice, values: true);

    /// <summary>
    /// The model price: the option value at the root node (<c>ValueLattice[0][0]</c>).
    /// </summary>
    public double RootValue
    {
        get
        {
            Validate();
            return ValueLattice[0][0];
        }
    }

    /// <summary>
    /// Always <see langword="true"/>: a constructed valued lattice is, by definition, the
    /// output of a completed backward-induction pass. A failed valuation produces no
    /// <see cref="LatticeValuationResult"/> (the producing pricer throws or returns an
    /// unsuccessful <c>PriceResult</c> before a lattice is paired), so there is no
    /// unsuccessful state for this type to represent.
    /// </summary>
    public bool IsSuccess
    {
        get
        {
            Validate();
            return true;
        }
    }

    internal void Validate()
    {
        TreeValidation.Validate(Tree);
        if (ValueLattice.Count != Tree.Steps + 1)
        {
            throw new ArgumentException("The option-value lattice must match the tree's layer count.", nameof(ValueLattice));
        }
    }
}
