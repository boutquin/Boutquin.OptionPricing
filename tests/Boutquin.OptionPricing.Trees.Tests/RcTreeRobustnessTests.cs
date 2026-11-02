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

using Boutquin.Curves.Abstractions.Identifiers;
using Boutquin.Curves.Core.Discounting;
using Boutquin.Domain.Enumerations;
using Boutquin.Domain.Extensions;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Trees.Binomial;
using Boutquin.OptionPricing.Trees.Pricing;
using FluentAssertions;

namespace Boutquin.OptionPricing.Trees.Tests;

public sealed class RcTreeRobustnessTests
{
    private static readonly DateOnly s_today = new(2026, 1, 2);
    private static OptionMarketData Market() => new(s_today, Currency.USD.Amount(100m), 0.2d,
        new FlatDiscountCurve(new CurveName("R"), s_today, Currency.USD, 0.05d));

    private static BinomialTree Tree() => new(2, 0.5d, 1.1d, 0.9d, 0.5d,
        new IReadOnlyList<double>[] { new[] { 100d }, new[] { 110d, 90d }, new[] { 121d, 99d, 81d } });

    [Fact]
    public void EveryBuilder_RejectsNullAndInvalidCountsBeforeAllocation()
    {
        foreach (ITreeBuilder builder in new ITreeBuilder[] { new CoxRossRubinsteinTreeBuilder(), new JarrowRuddTreeBuilder(), new FlexibleBinomialTreeBuilder(), new FlexibleBinomialTreeBuilder(TreeParameterization.JarrowRudd) })
        {
            FluentActions.Invoking(() => builder.Build(null!)).Should().Throw<ArgumentNullException>();
            foreach (int steps in new[] { 0, -1, TreeBuilderLimits.MaxSteps + 1, int.MaxValue })
            {
                var input = TreeModelInput.FromMarketData(Market(), 100m, s_today.AddDays(365), steps);
                FluentActions.Invoking(() => builder.Build(input)).Should().Throw<ArgumentOutOfRangeException>();
            }
        }
    }

    [Fact]
    public void ArrowDebreu_UsesEveryNodeProbability()
    {
        var tree = Tree() with { ProbabilityLattice = new IReadOnlyList<double>[] { new[] { 0.25d }, new[] { 0.8d, 0.4d } } };
        // Enumerate the four paths: UU=.2, UD=.05, DU=.3, DD=.45.
        var actual = new ArrowDebreuStatePriceCalculator().CalculateTerminalStatePrices(tree, 0.05d);
        double discount = Math.Exp(-0.05d);
        actual[0].Should().BeApproximately(0.2d * discount, 1e-12d);
        actual[1].Should().BeApproximately(0.35d * discount, 1e-12d);
        actual[2].Should().BeApproximately(0.45d * discount, 1e-12d);
        actual.Sum().Should().BeApproximately(discount, 1e-12d);
    }

    [Fact]
    public void EveryTreePricingEntryPoint_ConsumesTheNodeProbabilities()
    {
        var tree = Tree() with { ProbabilityLattice = new IReadOnlyList<double>[] { new[] { 0.25d }, new[] { 0.8d, 0.4d } }, RiskFreeRate = 0.05d };
        var european = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_today.AddDays(365));
        var american = new AmericanOption(CallPut.Call, 100m, Currency.USD, s_today.AddDays(365));
        // Only the UU path pays: probability .25*.8=.2 and payoff 121-100=21.
        double expected = 4.2d * Math.Exp(-0.05d);
        ((double)new EuropeanTreePricer().Price(european, Market(), tree).Price.Amount).Should().BeApproximately(expected, 1e-12d);
        new EuropeanTreePricer().PriceWithLattice(european, Market(), tree).RootValue.Should().BeApproximately(expected, 1e-12d);
        ((double)new AmericanTreePricer().Price(american, Market(), tree).Price.Amount).Should().BeApproximately(expected, 1e-12d);
        new AmericanTreePricer().PriceWithLattice(american, Market(), tree).RootValue.Should().BeApproximately(expected, 1e-12d);
        new AmericanTreePricer().ComputeExerciseBoundary(american, Market(), tree).Boundaries.Should().BeEmpty();
    }

    [Fact]
    public void Tree_UnrepresentableRoot_ReturnsUnsupportedInsteadOfConversionOverflow()
    {
        var tree = Tree() with { RiskFreeRate = -800d };
        var expiry = s_today.AddDays(365);
        var european = new EuropeanTreePricer().Price(new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry), Market(), tree);
        var american = new AmericanTreePricer().Price(new AmericanOption(CallPut.Call, 100m, Currency.USD, expiry), Market(), tree);
        european.Status.Should().Be(CalculationStatus.UnsupportedScenario);
        american.Status.Should().Be(CalculationStatus.UnsupportedScenario);
    }

    [Fact]
    public void Tree_OwnsBothNestedLattices()
    {
        var spot = new[] { 100d };
        var p = new[] { 0.5d };
        var tree = new BinomialTree(1, 1d, 1.1d, 0.9d, 0.5d,
            new IReadOnlyList<double>[] { spot, new[] { 110d, 90d } })
        { ProbabilityLattice = new IReadOnlyList<double>[] { p } };
        spot[0] = 1d;
        p[0] = 0d;
        tree.SpotLattice[0][0].Should().Be(100d);
        tree.ProbabilityLattice![0][0].Should().Be(0.5d);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Builders_RejectNonfiniteVolatility(double volatility)
    {
        foreach (ITreeBuilder builder in new ITreeBuilder[] { new CoxRossRubinsteinTreeBuilder(), new JarrowRuddTreeBuilder(), new FlexibleBinomialTreeBuilder(), new FlexibleBinomialTreeBuilder(TreeParameterization.JarrowRudd) })
        {
            var input = TreeModelInput.FromMarketData(Market(), 100m, s_today.AddDays(365), 2) with { Volatility = volatility };
            FluentActions.Invoking(() => builder.Build(input)).Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Fact]
    public void TreeConsumers_RejectMalformedGeometryAndProbabilities()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_today.AddDays(365));
        foreach (Func<BinomialTree> make in new Func<BinomialTree>[]
        {
            () => Tree() with { Steps = 3 },
            () => Tree() with { Steps = 1 },
            () => Tree() with { DeltaTime = 0d },
            () => Tree() with { RiskNeutralProbability = double.NaN },
            () => Tree() with { ProbabilityLattice = new IReadOnlyList<double>[] { new[] { 0.5d } } },
            () => Tree() with { RiskFreeRate = double.PositiveInfinity },
        })
        {
            FluentActions.Invoking(() => new EuropeanTreePricer().Price(option, Market(), make())).Should().Throw<ArgumentException>();
        }
    }

    [Fact]
    public void ValuedLattice_OwnsValuesAndRejectsNonfiniteGreeks()
    {
        var values = new[] { 10d };
        var tree = Tree() with { SpotLattice = new IReadOnlyList<double>[] { new[] { 100d }, new[] { 110d, 90d }, new[] { 121d, 100d, 81d } } };
        var valued = new LatticeValuationResult(tree, new IReadOnlyList<double>[] { values, new[] { 15d, 5d }, new[] { 21d, 0d, 0d } });
        values[0] = 99d;
        valued.RootValue.Should().Be(10d);
        FluentActions.Invoking(() => new TreeGreeksCalculator().Compute(valued with { ValueLattice = new IReadOnlyList<double>[] { new[] { double.NaN }, new[] { 15d, 5d }, new[] { 21d, 0d, 0d } } })).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MinimalGreeks_PinTheActualDividedDifferences()
    {
        var tree = Tree() with { SpotLattice = new IReadOnlyList<double>[] { new[] { 100d }, new[] { 110d, 90d }, new[] { 121d, 100d, 81d } } };
        var values = new IReadOnlyList<double>[] { new[] { 10d }, new[] { 15d, 5d }, new[] { 21d, 0d, 0d } };
        var greeks = new TreeGreeksCalculator().Compute(new(tree, values));
        greeks.Delta.Should().BeApproximately(0.5d, 1e-12d);
        greeks.Gamma.Should().BeApproximately(0.05d, 1e-12d);
        greeks.Theta.Should().BeApproximately(-10d / 365d, 1e-12d);
    }
}
