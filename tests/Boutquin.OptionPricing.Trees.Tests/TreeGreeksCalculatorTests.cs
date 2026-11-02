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
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Trees.Binomial;
using Boutquin.OptionPricing.Trees.Pricing;
using FluentAssertions;

namespace Boutquin.OptionPricing.Trees.Tests;

public sealed class TreeGreeksCalculatorTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 6, 15);

    // AC-1 (G5): European tree Delta/Gamma/Theta converge to analytical Black-Scholes Greeks
    // within 1% at >= 200 steps. The lattice finite differences (Chriss Figure 7.8.1 / §7.8) must
    // reproduce the closed-form sensitivities to leading order.
    [Fact]
    public void EuropeanCall_DeltaGammaTheta_ConvergeToAnalyticalWithinOnePercent()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var market = CreateMarket(100m, 0.20d, 0.05d, 0.0d);

        // Analytical reference (delta in [-1,1], gamma in spot^-1, theta per calendar day).
        var analytic = new BlackScholesGreeks().CalculateGreeks(option, market);

        var input = TreeModelInput.FromMarketData(market, option.Strike.Amount, option.ExpiryDate, 400);
        var tree = new CoxRossRubinsteinTreeBuilder().Build(input);
        var valued = new EuropeanTreePricer().PriceWithLattice(option, market, tree);

        var greeks = new TreeGreeksCalculator().Compute(valued);

        greeks.Delta.Should().NotBeNull();
        greeks.Gamma.Should().NotBeNull();
        greeks.Theta.Should().NotBeNull();

        RelativeError(greeks.Delta!.Value, analytic.Delta!.Value).Should().BeLessThan(0.01d);
        RelativeError(greeks.Gamma!.Value, analytic.Gamma!.Value).Should().BeLessThan(0.01d);
        RelativeError(greeks.Theta!.Value, analytic.Theta!.Value).Should().BeLessThan(0.01d);
    }

    [Fact]
    public void EuropeanPut_DeltaGammaTheta_ConvergeToAnalyticalWithinOnePercent()
    {
        var option = new EuropeanOption(CallPut.Put, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var market = CreateMarket(100m, 0.20d, 0.05d, 0.0d);

        var analytic = new BlackScholesGreeks().CalculateGreeks(option, market);

        var input = TreeModelInput.FromMarketData(market, option.Strike.Amount, option.ExpiryDate, 400);
        var tree = new CoxRossRubinsteinTreeBuilder().Build(input);
        var valued = new EuropeanTreePricer().PriceWithLattice(option, market, tree);

        var greeks = new TreeGreeksCalculator().Compute(valued);

        RelativeError(greeks.Delta!.Value, analytic.Delta!.Value).Should().BeLessThan(0.01d);
        RelativeError(greeks.Gamma!.Value, analytic.Gamma!.Value).Should().BeLessThan(0.01d);
        RelativeError(greeks.Theta!.Value, analytic.Theta!.Value).Should().BeLessThan(0.01d);
    }

    // The lattice method only produces Delta/Gamma/Theta; Vega and Rho require repricing
    // and are intentionally left null (documented out-of-scope per spec).
    [Fact]
    public void Compute_LeavesVegaAndRhoNull()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var market = CreateMarket(100m, 0.20d, 0.05d, 0.0d);
        var input = TreeModelInput.FromMarketData(market, option.Strike.Amount, option.ExpiryDate, 200);
        var tree = new CoxRossRubinsteinTreeBuilder().Build(input);
        var valued = new EuropeanTreePricer().PriceWithLattice(option, market, tree);

        var greeks = new TreeGreeksCalculator().Compute(valued);

        greeks.Vega.Should().BeNull();
        greeks.Rho.Should().BeNull();
    }

    // AC-2 (G5 sibling): American put Delta in [-1, 0], Gamma > 0. A put's delta is negative
    // (value falls as spot rises) and bounded below by -1; gamma is positive (convexity).
    [Fact]
    public void AmericanPut_DeltaInNegativeUnitInterval_GammaPositive()
    {
        var option = new AmericanOption(CallPut.Put, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var market = CreateMarket(100m, 0.30d, 0.05d, 0.0d);

        var input = TreeModelInput.FromMarketData(market, option.Strike.Amount, option.ExpiryDate, 300);
        var tree = new CoxRossRubinsteinTreeBuilder().Build(input);
        var valued = new AmericanTreePricer().PriceWithLattice(option, market, tree);

        var greeks = new TreeGreeksCalculator().Compute(valued);

        greeks.Delta!.Value.Should().BeInRange(-1.0d, 0.0d);
        greeks.Gamma!.Value.Should().BeGreaterThan(0.0d);
    }

    // AC-3: Delta/Gamma/Theta are finite (no NaN/Infinity) even at the minimal step count
    // the calculator supports (a tree of exactly 2 steps, the smallest lattice the Figure
    // 7.8.1 / §7.8 finite differences can read).
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void MinimalStepCounts_ProduceFiniteGreeks(int steps)
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var market = CreateMarket(100m, 0.20d, 0.05d, 0.0d);

        var input = TreeModelInput.FromMarketData(market, option.Strike.Amount, option.ExpiryDate, steps);
        var tree = new CoxRossRubinsteinTreeBuilder().Build(input);
        var valued = new EuropeanTreePricer().PriceWithLattice(option, market, tree);

        var greeks = new TreeGreeksCalculator().Compute(valued);

        double.IsFinite(greeks.Delta!.Value).Should().BeTrue();
        double.IsFinite(greeks.Gamma!.Value).Should().BeTrue();
        double.IsFinite(greeks.Theta!.Value).Should().BeTrue();
    }

    // AC-4 (F-L6-01): the lattice Theta finite difference (V(2,centre) − V(0,0))/(2·dt)
    // is only valid when the step-2 centre node sits at the root spot (S(2,centre) == S0),
    // which holds for CRR (u·d = 1) but NOT for Jarrow-Rudd (u·d = exp(2·drift) ≠ 1), whose
    // centre node is at a drifted spot. Feeding a non-spot-centered (JR) lattice to Compute
    // must throw a clear exception rather than silently return a ~22% biased Theta.
    [Fact]
    public void Compute_NonSpotCenteredLattice_Throws()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var market = CreateMarket(100m, 0.20d, 0.05d, 0.0d);

        var input = TreeModelInput.FromMarketData(market, option.Strike.Amount, option.ExpiryDate, 200);
        var tree = new JarrowRuddTreeBuilder().Build(input);
        var valued = new EuropeanTreePricer().PriceWithLattice(option, market, tree);

        var act = () => new TreeGreeksCalculator().Compute(valued);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*spot-centered*");
    }

    [Fact]
    public void Compute_NullLattice_Throws()
    {
        var act = () => new TreeGreeksCalculator().Compute(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Compute_TreeWithFewerThanTwoSteps_Throws()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var market = CreateMarket(100m, 0.20d, 0.05d, 0.0d);
        var input = TreeModelInput.FromMarketData(market, option.Strike.Amount, option.ExpiryDate, 1);
        var tree = new CoxRossRubinsteinTreeBuilder().Build(input);
        var valued = new EuropeanTreePricer().PriceWithLattice(option, market, tree);

        var act = () => new TreeGreeksCalculator().Compute(valued);
        act.Should().Throw<ArgumentException>();
    }

    private static double RelativeError(double actual, double expected)
        => Math.Abs(actual - expected) / Math.Max(Math.Abs(expected), 1e-12d);

    private static OptionMarketData CreateMarket(decimal spot, double flatVolatility, double riskFreeRate, double dividendYield)
    {
        return new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(spot),
            flatVolatility,
            new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, Currency.USD, riskFreeRate),
            new FlatDiscountCurve(new CurveName("DIVIDEND"), s_valuationDate, Currency.USD, dividendYield));
    }
}
