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
using Boutquin.OptionPricing.Analytics.Parity;

namespace Boutquin.OptionPricing.Tests.Verification;

/// <summary>
/// Cross-language verification for <see cref="PutCallParity"/> algebra and
/// <see cref="RiskNeutralValuation.ForwardPrice(double, double, double, double)"/>.
/// Both are pure-arithmetic identities — the Python reference matches at
/// <see cref="CrossLanguageVerificationBase.PrecisionExact"/>.
/// </summary>
public sealed class ParityVerificationTests : CrossLanguageVerificationBase
{
    public static IEnumerable<object[]> GetScenarios(string section)
    {
        using var doc = LoadVector("parity");
        var array = doc.RootElement.GetProperty(section).EnumerateArray();
        var index = 0;
        foreach (var entry in array)
        {
            yield return new object[] { index++, JsonSerializer.Serialize(entry) };
        }
    }

    public static IEnumerable<object[]> PutCallParityScenarios() => GetScenarios("put_call_parity");
    public static IEnumerable<object[]> ForwardScenarios() => GetScenarios("forward_prices");

    [Theory]
    [MemberData(nameof(PutCallParityScenarios))]
    public void ImpliedCallPrice_FromPut_MatchesReference(int index, string scenarioJson)
    {
        var s = JsonDocument.Parse(scenarioJson).RootElement;
        var put = s.GetProperty("observed_put").GetDecimal();
        var spot = s.GetProperty("spot").GetDecimal();
        var strike = s.GetProperty("strike").GetDecimal();
        var dfRf = s.GetProperty("df_rf").GetDouble();
        var dfDiv = s.GetProperty("df_div").GetDouble();
        var expected = s.GetProperty("implied_call_from_put").GetDouble();

        var actual = (double)PutCallParity.ImpliedCallPrice(put, spot, strike, dfRf, dfDiv);

        AssertWithin(actual, expected, PrecisionExact, $"parity[{index}] implied call");
    }

    [Theory]
    [MemberData(nameof(PutCallParityScenarios))]
    public void ImpliedPutPrice_FromCall_MatchesReference(int index, string scenarioJson)
    {
        var s = JsonDocument.Parse(scenarioJson).RootElement;
        var call = s.GetProperty("observed_call").GetDecimal();
        var spot = s.GetProperty("spot").GetDecimal();
        var strike = s.GetProperty("strike").GetDecimal();
        var dfRf = s.GetProperty("df_rf").GetDouble();
        var dfDiv = s.GetProperty("df_div").GetDouble();
        var expected = s.GetProperty("implied_put_from_call").GetDouble();

        var actual = (double)PutCallParity.ImpliedPutPrice(call, spot, strike, dfRf, dfDiv);

        AssertWithin(actual, expected, PrecisionExact, $"parity[{index}] implied put");
    }

    [Theory]
    [MemberData(nameof(ForwardScenarios))]
    public void ForwardPrice_MatchesReference(int index, string scenarioJson)
    {
        var s = JsonDocument.Parse(scenarioJson).RootElement;
        var spot = s.GetProperty("spot").GetDouble();
        var dfRf = s.GetProperty("df_rf").GetDouble();
        var dfDiv = s.GetProperty("df_div").GetDouble();
        var dfBorrow = s.GetProperty("df_borrow").GetDouble();
        var expected = s.GetProperty("forward_price").GetDouble();

        var actual = RiskNeutralValuation.ForwardPrice(spot, dfRf, dfDiv, dfBorrow);

        AssertWithin(actual, expected, PrecisionExact, $"forward[{index}]");
    }
}
