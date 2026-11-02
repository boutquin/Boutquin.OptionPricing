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

public sealed class TreeConvergenceTests
{
    [Fact]
    public void EuropeanTreePrice_ShouldApproachBlackScholes()
    {
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);

        var analytic = new BlackScholesPricer().Price(option, market).Price.Amount;
        var input = TreeModelInput.FromMarketData(market, option.Strike.Amount, option.ExpiryDate, 400);
        var tree = new CoxRossRubinsteinTreeBuilder().Build(input);
        var treePrice = new EuropeanTreePricer().Price(option, market, tree).Price.Amount;

        treePrice.Should().BeApproximately(analytic, 0.10m);
    }

    [Theory]
    [InlineData(TreeParameterization.CoxRossRubinstein)]
    [InlineData(TreeParameterization.JarrowRudd)]
    public void TreePriceWithNonZeroBorrow_ShouldApproachBlackScholes(TreeParameterization parameterization)
    {
        // Regression guard for the borrow-sign carry convention (F-L6-01/02). A stock-borrow
        // cost behaves like an additional yield that LOWERS the forward, so the library-wide
        // convention is effectiveQ = q + b (BlackScholesPricer, RiskNeutralValuation,
        // OptionMarketData.ForwardPrice). The tree builders must use the same q + b carry so
        // the tree converges to the Black-Scholes price even when borrow != 0. With the wrong
        // q - b sign the tree converges ~46% off (CRR/JR ~ 11.10 vs BS ~ 7.58 at borrow=0.03),
        // which this assertion catches; with borrow=0 the bug is invisible (q - 0 == q + 0),
        // so the borrow curve is essential to the regression.
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.05d, 0.02d, 0.03d);

        var analytic = new BlackScholesPricer().Price(option, market).Price.Amount;
        var input = TreeModelInput.FromMarketData(market, option.Strike.Amount, option.ExpiryDate, 400);
        var tree = new FlexibleBinomialTreeBuilder(parameterization).Build(input);
        var treePrice = new EuropeanTreePricer().Price(option, market, tree).Price.Amount;

        treePrice.Should().BeApproximately(analytic, 0.10m);
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

    private static OptionMarketData CreateMarket(DateOnly valuationDate, decimal spot, double flatVolatility, double riskFreeRate, double dividendYield, double borrowRate)
    {
        return new OptionMarketData(
            valuationDate,
            Currency.USD.Amount(spot),
            flatVolatility,
            new FlatDiscountCurve(new CurveName("USD-OIS"), valuationDate, Currency.USD, riskFreeRate),
            new FlatDiscountCurve(new CurveName("DIVIDEND"), valuationDate, Currency.USD, dividendYield),
            new FlatDiscountCurve(new CurveName("BORROW"), valuationDate, Currency.USD, borrowRate));
    }
}
