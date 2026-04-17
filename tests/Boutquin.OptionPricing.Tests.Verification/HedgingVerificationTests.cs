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
using Boutquin.OptionPricing.Analytics.Hedging;

namespace Boutquin.OptionPricing.Tests.Verification;

/// <summary>
/// Cross-language verification for <see cref="DeltaHedgeSimulator"/>. Because the
/// simulator walks a pre-specified path (not an internally-drawn RNG), both C#
/// and Python run the identical algorithm and must agree at
/// <see cref="CrossLanguageVerificationBase.PrecisionNumeric"/> on the full
/// delta sequence, terminal portfolio value, and hedging error.
/// </summary>
public sealed class HedgingVerificationTests : CrossLanguageVerificationBase
{
    public static IEnumerable<object[]> Scenarios()
    {
        using var doc = LoadVector("hedging");
        var index = 0;
        foreach (var entry in doc.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            yield return new object[] { index++, JsonSerializer.Serialize(entry) };
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void DeltaHedgeSimulator_MatchesPythonReference(int index, string scenarioJson)
    {
        var s = JsonDocument.Parse(scenarioJson).RootElement;
        var valuationDate = ParseDate(s.GetProperty("valuation_date").GetString()!);
        var days = s.GetProperty("days_to_expiry").GetInt32();
        var expiry = valuationDate.AddDays(days);
        var spot0 = s.GetProperty("spot0").GetDecimal();
        var strike = s.GetProperty("strike").GetDecimal();
        var sigma = s.GetProperty("sigma").GetDouble();
        var r = s.GetProperty("r").GetDouble();
        var q = s.GetProperty("q").GetDouble();
        var callPut = s.GetProperty("call_put").GetString()!;

        var market = BuildMarket(valuationDate, spot0, sigma, r, q);
        var option = BuildEuropeanOption(strike, expiry, callPut);

        var pathSpots = s.GetProperty("path_spots").EnumerateArray()
            .Select(e => (decimal)e.GetDouble())
            .ToList();

        var request = new DeltaHedgeSimulationRequest(option, market, pathSpots);
        var result = new DeltaHedgeSimulator().Simulate(request);

        // Start price.
        var expectedStart = s.GetProperty("expected_start_price").GetDouble();
        AssertWithin((double)result.StartingOptionPrice, expectedStart, PrecisionNumeric,
            $"hedge[{index}] start price");

        // Delta sequence.
        var expectedDeltas = s.GetProperty("expected_deltas").EnumerateArray()
            .Select(e => e.GetDouble()).ToArray();
        Assert.Equal(expectedDeltas.Length, result.Deltas.Count);
        for (int i = 0; i < expectedDeltas.Length; i++)
        {
            AssertWithin(result.Deltas[i], expectedDeltas[i], PrecisionNumeric,
                $"hedge[{index}] delta[{i}]");
        }

        // Option payoff (algebraic identity).
        var expectedPayoff = s.GetProperty("expected_option_payoff").GetDouble();
        AssertWithin((double)result.OptionPayoff, expectedPayoff, PrecisionNumeric,
            $"hedge[{index}] payoff");

        // Final portfolio value — the load-bearing quantity.
        var expectedPortfolio = s.GetProperty("expected_final_portfolio").GetDouble();
        AssertWithin((double)result.FinalPortfolioValue, expectedPortfolio, PrecisionNumeric,
            $"hedge[{index}] final portfolio");

        // Hedging error.
        var expectedError = s.GetProperty("expected_hedging_error").GetDouble();
        AssertWithin((double)result.HedgingError, expectedError, PrecisionNumeric,
            $"hedge[{index}] hedging error");
    }
}
