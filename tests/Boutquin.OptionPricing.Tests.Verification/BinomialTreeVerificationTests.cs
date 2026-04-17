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

using System.Text.Json;
using Boutquin.OptionPricing.Trees.Binomial;
using Boutquin.OptionPricing.Trees.Pricing;

namespace Boutquin.OptionPricing.Tests.Verification;

/// <summary>
/// Cross-language verification: asserts that CRR and Jarrow-Rudd binomial trees
/// plus the European and American backward-induction pricers agree with a
/// Python reference implementation of the same algorithm
/// (see <c>tests/Verification/generate_binomial_vectors.py</c>).
/// </summary>
/// <remarks>
/// Because the Python reference replicates the C# algorithm verbatim in double
/// arithmetic, matching prices must fall within
/// <see cref="CrossLanguageVerificationBase.PrecisionNumeric"/>. A separate
/// convergence section asserts that tree prices approach the Black-Scholes
/// analytic limit within an empirical bound — this is a convergence sanity
/// check, not a precision identity.
/// </remarks>
public sealed class BinomialTreeVerificationTests : CrossLanguageVerificationBase
{
    public static IEnumerable<object[]> GetScenarios(string section)
    {
        using var doc = LoadVector("binomial");
        var array = doc.RootElement.GetProperty(section).EnumerateArray();
        var index = 0;
        foreach (var entry in array)
        {
            yield return new object[] { index++, JsonSerializer.Serialize(entry) };
        }
    }

    public static IEnumerable<object[]> AllScenarios() => GetScenarios("scenarios");
    public static IEnumerable<object[]> ConvergenceScenarios() => GetScenarios("convergence");
    public static IEnumerable<object[]> ArrowDebreuScenarios() => GetScenarios("arrow_debreu");

    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void EuropeanTree_MatchesPythonReference(int index, string scenarioJson)
    {
        var (valuationDate, expiry, spot, strike, sigma, r, q, scheme, steps, callPut, scenario) =
            ParseScenario(scenarioJson);
        var expected = scenario.GetProperty("european_price").GetDouble();

        var market = BuildMarket(valuationDate, spot, sigma, r, q);
        var option = BuildEuropeanOption(strike, expiry, callPut);
        var tree = BuildTree(market, option.Strike, option.ExpiryDate, steps, scheme);

        var actual = (double)new EuropeanTreePricer().Price(option, market, tree).Price;

        AssertWithin(actual, expected, PrecisionNumeric,
            $"europe[{index}] {scheme} N={steps} ({callPut} K={strike} σ={sigma} r={r} q={q})");
    }

    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void AmericanTree_MatchesPythonReference(int index, string scenarioJson)
    {
        var (valuationDate, expiry, spot, strike, sigma, r, q, scheme, steps, callPut, scenario) =
            ParseScenario(scenarioJson);
        var expected = scenario.GetProperty("american_price").GetDouble();

        var market = BuildMarket(valuationDate, spot, sigma, r, q);
        var option = BuildAmericanOption(strike, expiry, callPut);
        var tree = BuildTree(market, option.Strike, option.ExpiryDate, steps, scheme);

        var actual = (double)new AmericanTreePricer().Price(option, market, tree).Price;

        AssertWithin(actual, expected, PrecisionNumeric,
            $"america[{index}] {scheme} N={steps} ({callPut} K={strike} σ={sigma} r={r} q={q})");
    }

    [Theory]
    [MemberData(nameof(ConvergenceScenarios))]
    public void EuropeanTree_ConvergesTowardBlackScholesAnalyticLimit(int index, string scenarioJson)
    {
        var (valuationDate, expiry, spot, strike, sigma, r, q, scheme, steps, callPut, scenario) =
            ParseScenario(scenarioJson);
        var analytic = scenario.GetProperty("analytic_price").GetDouble();
        var pythonTree = scenario.GetProperty("tree_price").GetDouble();

        var market = BuildMarket(valuationDate, spot, sigma, r, q);
        var option = BuildEuropeanOption(strike, expiry, callPut);
        var tree = BuildTree(market, option.Strike, option.ExpiryDate, steps, scheme);
        var csharpTree = (double)new EuropeanTreePricer().Price(option, market, tree).Price;

        // 1. C# matches Python tree to PRECISION_NUMERIC — they run the same algorithm.
        AssertWithin(csharpTree, pythonTree, PrecisionNumeric,
            $"conv[{index}] {scheme} N={steps} tree C# vs Python");

        // 2. C# tree approaches the BSM analytic. Convergence is O(1/N) for CRR / Jarrow-Rudd,
        //    with pronounced oscillations at coarse N. Loose empirical bounds:
        //        N=100: |tree - analytic| < 0.75 of spot
        //        N=400: |tree - analytic| < 0.25 of spot
        //    Bounds derived from numeric experiments on this scenario grid; tightening
        //    further requires Richardson extrapolation or smoothing which is out of
        //    scope for this verifier.
        double bound = steps >= 400 ? 0.25 : 0.75;
        AssertWithin(csharpTree, analytic, bound,
            $"conv[{index}] {scheme} N={steps} tree → analytic convergence (loose bound)");
    }

    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void FlexibleBuilder_DelegatesToSchemeSpecificBuilder(int index, string scenarioJson)
    {
        var (valuationDate, expiry, spot, strike, sigma, r, q, scheme, steps, callPut, scenario) =
            ParseScenario(scenarioJson);
        var expected = scenario.GetProperty("european_price").GetDouble();

        var market = BuildMarket(valuationDate, spot, sigma, r, q);
        var option = BuildEuropeanOption(strike, expiry, callPut);
        var input = TreeModelInput.FromMarketData(market, option.Strike, option.ExpiryDate, steps);
        var parameterization = scheme switch
        {
            "crr" => TreeParameterization.CoxRossRubinstein,
            "jr" => TreeParameterization.JarrowRudd,
            _ => throw new ArgumentOutOfRangeException(nameof(scenarioJson), scheme, "Unknown tree scheme."),
        };
        var tree = new FlexibleBinomialTreeBuilder(parameterization).Build(input);

        var actual = (double)new EuropeanTreePricer().Price(option, market, tree).Price;

        // Flexible wraps the same algorithm — match Python reference at PRECISION_NUMERIC.
        AssertWithin(actual, expected, PrecisionNumeric,
            $"flex[{index}] {scheme} N={steps} ({callPut} K={strike})");
    }

    [Theory]
    [MemberData(nameof(ArrowDebreuScenarios))]
    public void ArrowDebreu_TerminalStatePrices_MatchReferenceAndSumToDiscount(int index, string scenarioJson)
    {
        var scenario = JsonDocument.Parse(scenarioJson).RootElement;
        var valuationDate = ParseDate(scenario.GetProperty("valuation_date").GetString()!);
        var days = scenario.GetProperty("days_to_expiry").GetInt32();
        var expiry = valuationDate.AddDays(days);
        var spot = scenario.GetProperty("spot").GetDecimal();
        var sigma = scenario.GetProperty("sigma").GetDouble();
        var r = scenario.GetProperty("r").GetDouble();
        var q = scenario.GetProperty("q").GetDouble();
        var steps = scenario.GetProperty("steps").GetInt32();

        var market = BuildMarket(valuationDate, spot, sigma, r, q);
        // Use ATM strike; strike is irrelevant to AD state prices (they depend only on
        // tree structure), but TreeModelInput requires one.
        var input = TreeModelInput.FromMarketData(market, spot, expiry, steps);
        var tree = new CoxRossRubinsteinTreeBuilder().Build(input);

        var actualPrices = new ArrowDebreuStatePriceCalculator()
            .CalculateTerminalStatePrices(tree, market.RiskFreeRate(expiry));

        var expectedPrices = scenario.GetProperty("arrow_debreu_prices")
            .EnumerateArray()
            .Select(e => e.GetDouble())
            .ToArray();

        Assert.Equal(expectedPrices.Length, actualPrices.Count);
        for (int i = 0; i < expectedPrices.Length; i++)
        {
            AssertWithin(actualPrices[i], expectedPrices[i], PrecisionNumeric,
                $"ad[{index}] state[{i}]");
        }

        // Sanity: AD terminal state prices must sum to e^{-r T}.
        var expectedSum = scenario.GetProperty("expected_ad_sum").GetDouble();
        var actualSum = actualPrices.Sum();
        AssertWithin(actualSum, expectedSum, PrecisionNumeric,
            $"ad[{index}] sum = e^{{-r T}}");
    }

    private static BinomialTree BuildTree(
        Boutquin.OptionPricing.Domain.Market.OptionMarketData market,
        decimal strike,
        DateOnly expiry,
        int steps,
        string scheme)
    {
        var input = TreeModelInput.FromMarketData(market, strike, expiry, steps);
        return scheme switch
        {
            "crr" => new CoxRossRubinsteinTreeBuilder().Build(input),
            "jr" => new JarrowRuddTreeBuilder().Build(input),
            _ => throw new ArgumentOutOfRangeException(nameof(scheme), scheme, "Unknown tree scheme."),
        };
    }

    private static (DateOnly ValuationDate,
                    DateOnly Expiry,
                    decimal Spot,
                    decimal Strike,
                    double Sigma,
                    double R,
                    double Q,
                    string Scheme,
                    int Steps,
                    string CallPut,
                    JsonElement Scenario)
        ParseScenario(string scenarioJson)
    {
        var scenario = JsonDocument.Parse(scenarioJson).RootElement;
        var valuationDate = ParseDate(scenario.GetProperty("valuation_date").GetString()!);
        var days = scenario.GetProperty("days_to_expiry").GetInt32();
        var expiry = valuationDate.AddDays(days);
        var spot = scenario.GetProperty("spot").GetDecimal();
        var strike = scenario.GetProperty("strike").GetDecimal();
        var sigma = scenario.GetProperty("sigma").GetDouble();
        var r = scenario.GetProperty("r").GetDouble();
        var q = scenario.GetProperty("q").GetDouble();
        var scheme = scenario.GetProperty("scheme").GetString()!;
        var steps = scenario.GetProperty("steps").GetInt32();
        var callPut = scenario.GetProperty("call_put").GetString()!;
        return (valuationDate, expiry, spot, strike, sigma, r, q, scheme, steps, callPut, scenario);
    }
}
