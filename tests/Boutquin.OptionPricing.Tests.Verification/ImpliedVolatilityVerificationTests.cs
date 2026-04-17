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
using Boutquin.OptionPricing.Calibration.ImpliedVolatility;
using Boutquin.OptionPricing.Domain.Enums;

namespace Boutquin.OptionPricing.Tests.Verification;

/// <summary>
/// Cross-language verification for <see cref="ImpliedVolatilitySolver"/>. Each scenario
/// prices a European option at a known "true sigma," then asks the solver to recover
/// that sigma from the price. Both C# (Boutquin.Numerics <c>BrentSolver</c>) and the
/// Python reference (scipy.optimize.brentq) must return values that agree with the true
/// input at <see cref="CrossLanguageVerificationBase.PrecisionNumeric"/>.
/// </summary>
public sealed class ImpliedVolatilityVerificationTests : CrossLanguageVerificationBase
{
    public static IEnumerable<object[]> Scenarios()
    {
        using var doc = LoadVector("implied_volatility");
        var array = doc.RootElement.GetProperty("scenarios").EnumerateArray();
        var index = 0;
        foreach (var entry in array)
        {
            yield return new object[] { index++, JsonSerializer.Serialize(entry) };
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Solver_RecoversTrueSigma_AgreesWithScipyBrentq(int index, string scenarioJson)
    {
        var s = JsonDocument.Parse(scenarioJson).RootElement;
        var valuationDate = ParseDate(s.GetProperty("valuation_date").GetString()!);
        var days = s.GetProperty("days_to_expiry").GetInt32();
        var expiry = valuationDate.AddDays(days);
        var spot = s.GetProperty("spot").GetDecimal();
        var strike = s.GetProperty("strike").GetDecimal();
        var r = s.GetProperty("r").GetDouble();
        var q = s.GetProperty("q").GetDouble();
        var callPut = s.GetProperty("call_put").GetString()!;
        var trueSigma = s.GetProperty("true_sigma").GetDouble();
        var observedPrice = s.GetProperty("observed_price").GetDecimal();
        var scipyRecovered = s.GetProperty("recovered_sigma").GetDouble();

        // Seed the market with a neutral volatility — solver iterates on this axis.
        var market = BuildMarket(valuationDate, spot, 0.2d, r, q);
        var option = BuildEuropeanOption(strike, expiry, callPut);

        var request = new ImpliedVolatilityRequest(option, market, observedPrice, InitialGuess: 0.2d);
        var result = new ImpliedVolatilitySolver().Solve(request);

        Assert.True(result.IsSuccess, $"iv[{index}] solver status {result.Status}");
        Assert.NotNull(result.ImpliedVolatility);

        var csharpRecovered = result.ImpliedVolatility!.Value;

        // 1. C# matches the scipy reference root.
        AssertWithin(csharpRecovered, scipyRecovered, PrecisionNumeric,
            $"iv[{index}] C# vs scipy ({callPut} K={strike} T={days}d trueσ={trueSigma})");

        // 2. Both recover the true sigma used to generate the price.
        AssertWithin(csharpRecovered, trueSigma, PrecisionNumeric,
            $"iv[{index}] C# → true σ");
        AssertWithin(scipyRecovered, trueSigma, PrecisionNumeric,
            $"iv[{index}] scipy → true σ");
    }

    [Fact]
    public void Solver_RejectsNonEuropeanOption()
    {
        var valuationDate = ParseDate("2026-01-02");
        var expiry = valuationDate.AddDays(182);
        var market = BuildMarket(valuationDate, 100m, 0.2d, 0.04d, 0.0d);
        var american = BuildAmericanOption(100m, expiry, "Call");

        var request = new ImpliedVolatilityRequest(american, market, 5m, InitialGuess: 0.2d);
        var result = new ImpliedVolatilitySolver().Solve(request);

        Assert.Equal(CalculationStatus.UnsupportedScenario, result.Status);
        Assert.Null(result.ImpliedVolatility);
    }
}
