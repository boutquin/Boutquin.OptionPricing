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
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Exotics.Barriers;
using Boutquin.OptionPricing.Trees.Binomial;

namespace Boutquin.OptionPricing.Tests.Verification;

/// <summary>
/// Cross-language verification for the Barriers module: BarrierParity algebra,
/// BGK monitoring adjustment, and BarrierTreePricer backward-induction.
/// </summary>
public sealed class BarrierVerificationTests : CrossLanguageVerificationBase
{
    public static IEnumerable<object[]> GetScenarios(string section)
    {
        using var doc = LoadVector("barrier");
        var array = doc.RootElement.GetProperty(section).EnumerateArray();
        var index = 0;
        foreach (var entry in array)
        {
            yield return new object[] { index++, JsonSerializer.Serialize(entry) };
        }
    }

    public static IEnumerable<object[]> ParityScenarios() => GetScenarios("parity");
    public static IEnumerable<object[]> MonitoringScenarios() => GetScenarios("monitoring");
    public static IEnumerable<object[]> TreePricerScenarios() => GetScenarios("tree_pricer");
    public static IEnumerable<object[]> TreePricerBreachedScenarios() => GetScenarios("tree_pricer_breached");

    [Theory]
    [MemberData(nameof(ParityScenarios))]
    public void BarrierParity_IdentityHolds(int index, string scenarioJson)
    {
        _ = index; // parameter retained for failure labeling via xUnit output
        var s = JsonDocument.Parse(scenarioJson).RootElement;
        var vanilla = s.GetProperty("vanilla_price").GetDecimal();

        if (s.TryGetProperty("knock_out_price", out var koEl))
        {
            var ko = koEl.GetDecimal();
            var expected = s.GetProperty("implied_knock_in").GetDecimal();
            Assert.Equal(expected, BarrierParity.ImpliedKnockInPrice(vanilla, ko));
        }
        else
        {
            var ki = s.GetProperty("knock_in_price").GetDecimal();
            var expected = s.GetProperty("implied_knock_out").GetDecimal();
            Assert.Equal(expected, BarrierParity.ImpliedKnockOutPrice(vanilla, ki));
        }
    }

    [Theory]
    [MemberData(nameof(MonitoringScenarios))]
    public void MonitoringAdjustment_MatchesBGKReference(int index, string scenarioJson)
    {
        var s = JsonDocument.Parse(scenarioJson).RootElement;
        var level = s.GetProperty("barrier_level").GetDecimal();
        var direction = s.GetProperty("direction").GetString() switch
        {
            "Up" => BarrierDirection.Up,
            "Down" => BarrierDirection.Down,
            _ => throw new ArgumentOutOfRangeException(nameof(scenarioJson), "Unknown direction or knock type."),
        };
        var sigma = s.GetProperty("sigma").GetDouble();
        var t = s.GetProperty("T").GetDouble();
        var m = s.GetProperty("monitoring_dates").GetInt32();
        var expected = s.GetProperty("adjusted_barrier").GetDouble();

        var actual = (double)BarrierMonitoringAdjustments.AdjustedBarrierLevel(level, direction, sigma, t, m);

        // BGK uses a decimal cast of exp(...) so we tolerate a small margin due to
        // the double→decimal rounding on both ends.
        AssertWithin(actual, expected, 1e-10, $"monitoring[{index}]");
    }

    [Theory]
    [MemberData(nameof(TreePricerScenarios))]
    public void BarrierTreePricer_MatchesPythonReference(int index, string scenarioJson)
    {
        var s = JsonDocument.Parse(scenarioJson).RootElement;
        var (market, option, tree) = BuildBarrierContext(s);
        var expected = s.GetProperty("price").GetDouble();

        var actual = (double)new BarrierTreePricer().Price(option, market, tree).Price;

        AssertWithin(actual, expected, PrecisionNumeric,
            $"barrier[{index}] {option.Direction}{option.KnockType} ({option.CallPut} K={option.Strike} B={option.BarrierLevel})");
    }

    [Theory]
    [MemberData(nameof(TreePricerBreachedScenarios))]
    public void BarrierTreePricer_SpotBreachedEdgeCase(int index, string scenarioJson)
    {
        var s = JsonDocument.Parse(scenarioJson).RootElement;
        var (market, option, tree) = BuildBarrierContext(s);
        var expected = s.GetProperty("expected_price").GetDouble();

        var actual = (double)new BarrierTreePricer().Price(option, market, tree).Price;

        AssertWithin(actual, expected, PrecisionNumeric,
            $"breached[{index}] {s.GetProperty("case").GetString()}");
    }

    private static (Boutquin.OptionPricing.Domain.Market.OptionMarketData Market,
                    BarrierOption Option,
                    BinomialTree Tree)
        BuildBarrierContext(JsonElement scenario)
    {
        var s = scenario;
        var valuationDate = ParseDate(s.GetProperty("valuation_date").GetString()!);
        var days = s.GetProperty("days_to_expiry").GetInt32();
        var expiry = valuationDate.AddDays(days);
        var spot = s.GetProperty("spot").GetDecimal();
        var strike = s.GetProperty("strike").GetDecimal();
        var sigma = s.GetProperty("sigma").GetDouble();
        var r = s.GetProperty("r").GetDouble();
        var q = s.GetProperty("q").GetDouble();
        var barrier = s.GetProperty("barrier_level").GetDecimal();
        var steps = s.GetProperty("steps").GetInt32();
        var direction = s.GetProperty("direction").GetString() switch
        {
            "Up" => BarrierDirection.Up,
            "Down" => BarrierDirection.Down,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), "Unknown direction."),
        };
        var knock = s.GetProperty("knock_type").GetString() switch
        {
            "Out" => BarrierKnockType.Out,
            "In" => BarrierKnockType.In,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), "Unknown knock type."),
        };
        var callPut = ParseCallPut(s.GetProperty("call_put").GetString()!);

        var market = BuildMarket(valuationDate, spot, sigma, r, q);
        var option = new BarrierOption(callPut, strike, barrier, direction, knock, expiry);
        var input = TreeModelInput.FromMarketData(market, strike, expiry, steps);
        var tree = new CoxRossRubinsteinTreeBuilder().Build(input);
        return (market, option, tree);
    }
}
