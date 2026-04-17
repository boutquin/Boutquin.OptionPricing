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
using Boutquin.OptionPricing.Analytics.BlackScholes;

namespace Boutquin.OptionPricing.Tests.Verification;

/// <summary>
/// Cross-language verification: asserts that <see cref="BlackScholesPricer"/> and
/// <see cref="BlackScholesGreeks"/> agree with scipy.stats.norm-based reference
/// values (see <c>tests/Verification/generate_blackscholes_vectors.py</c>).
/// </summary>
/// <remarks>
/// Both the price and every Greek are asserted against the same unified-carry
/// reference (Hull §19.12: q_eff = q + b used symmetrically in d1, d2, every
/// e^{-qT} discount, and the qSN(d1) drift term in theta). Scenarios with
/// b ≠ 0 are exercised in the main grid — there is no separate borrow-only
/// section. Every scenario surfaces as its own <see cref="Xunit.TheoryAttribute"/>
/// case so individual failures name a specific parameter combination.
/// </remarks>
public sealed class BlackScholesVerificationTests : CrossLanguageVerificationBase
{
    public static IEnumerable<object[]> GetScenarios(string section)
    {
        using var doc = LoadVector("blackscholes");
        var array = doc.RootElement.GetProperty(section).EnumerateArray();
        var index = 0;
        foreach (var entry in array)
        {
            yield return new object[] { index++, JsonSerializer.Serialize(entry) };
        }
    }

    public static IEnumerable<object[]> AllScenarios() => GetScenarios("scenarios");
    public static IEnumerable<object[]> ParityScenarios() => GetScenarios("parity");

    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void Price_MatchesScipyReference(int index, string scenarioJson)
    {
        var (valuationDate, expiry, spot, strike, sigma, r, q, b, callPut, scenario) =
            ParseScenario(scenarioJson);
        var expected = scenario.GetProperty("price").GetDouble();

        var market = BuildMarket(valuationDate, spot, sigma, r, q, b);
        var option = BuildEuropeanOption(strike, expiry, callPut);

        var actual = (double)new BlackScholesPricer().Price(option, market).Price;

        AssertWithin(actual, expected, PrecisionNumeric,
            $"scenario[{index}] price ({callPut} K={strike} T={(expiry.DayNumber - valuationDate.DayNumber)}d σ={sigma} r={r} q={q} b={b})");
    }

    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void Greeks_MatchScipyReference(int index, string scenarioJson)
    {
        var (valuationDate, expiry, spot, strike, sigma, r, q, b, callPut, scenario) =
            ParseScenario(scenarioJson);

        var market = BuildMarket(valuationDate, spot, sigma, r, q, b);
        var option = BuildEuropeanOption(strike, expiry, callPut);

        var greeks = new BlackScholesGreeks().CalculateGreeks(option, market);

        var label = $"scenario[{index}] ({callPut} K={strike} T={(expiry.DayNumber - valuationDate.DayNumber)}d σ={sigma} r={r} q={q} b={b})";
        AssertWithin(greeks.Delta!.Value, scenario.GetProperty("delta").GetDouble(), PrecisionNumeric, $"{label} delta");
        AssertWithin(greeks.Gamma!.Value, scenario.GetProperty("gamma").GetDouble(), PrecisionNumeric, $"{label} gamma");
        AssertWithin(greeks.Vega!.Value, scenario.GetProperty("vega").GetDouble(), PrecisionNumeric, $"{label} vega");
        AssertWithin(greeks.Theta!.Value, scenario.GetProperty("theta").GetDouble(), PrecisionNumeric, $"{label} theta");
        AssertWithin(greeks.Rho!.Value, scenario.GetProperty("rho").GetDouble(), PrecisionNumeric, $"{label} rho");
    }

    [Theory]
    [MemberData(nameof(ParityScenarios))]
    public void PutCallParity_CallMinusPut_EqualsDiscountedSpotMinusDiscountedStrike(int index, string scenarioJson)
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
        var b = scenario.GetProperty("b").GetDouble();

        var market = BuildMarket(valuationDate, spot, sigma, r, q, b);
        var call = BuildEuropeanOption(strike, expiry, "Call");
        var put = BuildEuropeanOption(strike, expiry, "Put");

        var pricer = new BlackScholesPricer();
        var callPrice = (double)pricer.Price(call, market).Price;
        var putPrice = (double)pricer.Price(put, market).Price;

        var lhs = callPrice - putPrice;
        var rhs = scenario.GetProperty("parity_rhs").GetDouble();

        AssertWithin(lhs, rhs, PrecisionNumeric,
            $"parity[{index}] (K={strike} T={days}d b={b})");
    }

    private static (DateOnly ValuationDate,
                    DateOnly Expiry,
                    decimal Spot,
                    decimal Strike,
                    double Sigma,
                    double R,
                    double Q,
                    double B,
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
        var b = scenario.GetProperty("b").GetDouble();
        var callPut = scenario.GetProperty("call_put").GetString()!;
        return (valuationDate, expiry, spot, strike, sigma, r, q, b, callPut, scenario);
    }
}
