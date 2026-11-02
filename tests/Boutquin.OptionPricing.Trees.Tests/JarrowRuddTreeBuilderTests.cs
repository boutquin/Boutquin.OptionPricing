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

/// <summary>
/// Verifies the Jarrow-Rudd tree builder produces correct lattice parameters
/// and converges to Black-Scholes pricing.
/// </summary>
public sealed class JarrowRuddTreeBuilderTests
{
    [Fact]
    public void Build_ShouldSetProbabilityToOneHalf()
    {
        var tree = BuildJarrowRuddTree(steps: 50);

        tree.RiskNeutralProbability.Should().BeApproximately(0.5d, 1e-12d);
    }

    [Fact]
    public void Build_UpAndDownFactors_ShouldSatisfyDriftCondition()
    {
        // JR: u = exp(drift + sigma*sqrt(dt)), d = exp(drift - sigma*sqrt(dt))
        // so ln(u) + ln(d) = 2 * drift
        var tree = BuildJarrowRuddTree(steps: 100);
        double logU = Math.Log(tree.UpFactor);
        double logD = Math.Log(tree.DownFactor);

        double r = 0.03d;
        double q = 0.01d;
        double sigma = 0.20d;
        double drift = (r - q - (0.5d * sigma * sigma)) * tree.DeltaTime;
        double expectedSum = 2d * drift;

        (logU + logD).Should().BeApproximately(expectedSum, 1e-10d);
    }

    [Fact]
    public void JarrowRuddTree_ShouldConvergeToBlackScholes()
    {
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);

        decimal analytic = new BlackScholesPricer().Price(option, market).Price.Amount;
        var input = TreeModelInput.FromMarketData(market, option.Strike.Amount, option.ExpiryDate, 400);
        var tree = new JarrowRuddTreeBuilder().Build(input);
        decimal treePrice = new EuropeanTreePricer().Price(option, market, tree).Price.Amount;

        treePrice.Should().BeApproximately(analytic, 0.10m);
    }

    [Fact]
    public void FlexibleBuilder_JarrowRudd_ShouldMatchDirectBuilder()
    {
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);
        var input = TreeModelInput.FromMarketData(market, 100m, valuationDate.AddMonths(6), 100);

        var direct = new JarrowRuddTreeBuilder().Build(input);
        var flexible = new FlexibleBinomialTreeBuilder(TreeParameterization.JarrowRudd).Build(input);

        flexible.UpFactor.Should().BeApproximately(direct.UpFactor, 1e-12d);
        flexible.DownFactor.Should().BeApproximately(direct.DownFactor, 1e-12d);
        flexible.RiskNeutralProbability.Should().BeApproximately(direct.RiskNeutralProbability, 1e-12d);
    }

    [Fact]
    public void FlexibleBuilder_DefaultCrr_ShouldMatchCrrBuilder()
    {
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);
        var input = TreeModelInput.FromMarketData(market, 100m, valuationDate.AddMonths(6), 100);

        var crr = new CoxRossRubinsteinTreeBuilder().Build(input);
        var flexible = new FlexibleBinomialTreeBuilder().Build(input);

        flexible.UpFactor.Should().BeApproximately(crr.UpFactor, 1e-12d);
        flexible.DownFactor.Should().BeApproximately(crr.DownFactor, 1e-12d);
        flexible.RiskNeutralProbability.Should().BeApproximately(crr.RiskNeutralProbability, 1e-12d);
    }

    private static BinomialTree BuildJarrowRuddTree(int steps)
    {
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);
        var input = TreeModelInput.FromMarketData(market, 100m, valuationDate.AddMonths(6), steps);
        return new JarrowRuddTreeBuilder().Build(input);
    }

    private static OptionMarketData CreateMarket(DateOnly valuationDate, decimal spot, double flatVolatility, double riskFreeRate, double dividendYield)
    {
        return new OptionMarketData(
            valuationDate,
            Currency.USD.Amount(spot),
            flatVolatility,
            new FlatDiscountCurve(new CurveName("USD-OIS"), valuationDate, Currency.USD, riskFreeRate),
            new FlatDiscountCurve(new CurveName("DIVIDEND"), valuationDate, Currency.USD, dividendYield));
    }
}
