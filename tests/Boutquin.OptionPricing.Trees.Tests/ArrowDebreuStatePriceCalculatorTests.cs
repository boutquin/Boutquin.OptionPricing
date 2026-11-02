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
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Trees.Binomial;
using FluentAssertions;

namespace Boutquin.OptionPricing.Trees.Tests;

/// <summary>
/// Tests for <see cref="ArrowDebreuStatePriceCalculator"/>: the no-arbitrage sum-to-discount
/// property, the null/probability guards (F16), and large-N numerical stability provided by
/// the log-space implementation (F16 — the naive product overflows past N ≈ 1030).
/// </summary>
public sealed class ArrowDebreuStatePriceCalculatorTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 15);
    private const Currency Usd = Currency.USD;

    private static (BinomialTree Tree, double Rate, double DiscountFactor) BuildTree(int steps, double r)
    {
        var expiry = s_valuationDate.AddMonths(6);
        var market = new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(100m),
            flatVolatility: 0.20,
            discountCurve: new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, Usd, r));
        var input = TreeModelInput.FromMarketData(market, 100m, expiry, steps);
        var tree = new CoxRossRubinsteinTreeBuilder().Build(input);
        double rate = market.RiskFreeRate(expiry);
        double df = Math.Exp(-rate * tree.DeltaTime * tree.Steps);
        return (tree, rate, df);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(200)]
    public void TerminalStatePrices_SumToDiscountFactor(int steps)
    {
        var (tree, rate, df) = BuildTree(steps, 0.05);

        IReadOnlyList<double> prices = new ArrowDebreuStatePriceCalculator().CalculateTerminalStatePrices(tree, rate);

        prices.Sum().Should().BeApproximately(df, 1e-9);
        prices.Should().OnlyContain(p => p >= 0d);
    }

    [Fact]
    public void TerminalStatePrices_LargeN_RemainsFiniteAndArbitrageFree()
    {
        // 1500 steps: C(1500, 750) overflows double, so the naive product produced NaN/Inf.
        // The log-space form must stay finite and still sum to the discount factor.
        var (tree, rate, df) = BuildTree(1500, 0.04);

        IReadOnlyList<double> prices = new ArrowDebreuStatePriceCalculator().CalculateTerminalStatePrices(tree, rate);

        prices.Should().OnlyContain(p => double.IsFinite(p) && p >= 0d);
        prices.Sum().Should().BeApproximately(df, 1e-9);
    }

    [Fact]
    public void CalculateTerminalStatePrices_NullTree_Throws()
    {
        var calculator = new ArrowDebreuStatePriceCalculator();

        calculator.Invoking(c => c.CalculateTerminalStatePrices(null!, 0.05))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CalculateTerminalStatePrices_DegenerateProbability_Throws()
    {
        // A tree whose risk-neutral probability is outside (0, 1) is ill-defined for state pricing.
        Action make = () => new BinomialTree(
            Steps: 2,
            DeltaTime: 0.25,
            UpFactor: 1.1,
            DownFactor: 1d / 1.1,
            RiskNeutralProbability: 1.5,
            SpotLattice: new IReadOnlyList<double>[]
            {
                new[] { 100d },
                new[] { 110d, 90d },
                new[] { 121d, 100d, 81d },
            });

        make.Should().Throw<ArgumentOutOfRangeException>();
    }
}
