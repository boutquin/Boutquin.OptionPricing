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
using Boutquin.Numerics.Random;
using Boutquin.OptionPricing.Analytics.StochasticVolatility;
using Boutquin.OptionPricing.Domain.Market;
using Xunit.Abstractions;

namespace Boutquin.OptionPricing.Tests.Verification;

public sealed class HestonQuantLibRcVerificationTests(ITestOutputHelper output) : CrossLanguageVerificationBase
{
    public static IEnumerable<object[]> Scenarios()
    {
        using var doc = LoadVector("quantlib-heston-rc");
        Assert.Equal("1.40", doc.RootElement.GetProperty("version").GetString());
        Assert.Equal("AnalyticHestonEngine", doc.RootElement.GetProperty("engine").GetString());
        foreach (var scenario in doc.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            yield return new object[] { scenario.GetProperty("id").GetInt32(), scenario.GetRawText() };
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void EulerMonteCarlo_AgreesWithIndependentCharacteristicFunctionOracle(int id, string scenarioJson)
    {
        using var document = JsonDocument.Parse(scenarioJson);
        var s = document.RootElement;
        var valuation = new DateOnly(2026, 4, 9);
        var expiry = valuation.AddDays(s.GetProperty("days").GetInt32());
        var market = BuildMarket(valuation, s.GetProperty("spot").GetDecimal(), 0.2d,
            s.GetProperty("r").GetDouble(), s.GetProperty("q").GetDouble(), s.GetProperty("b").GetDouble());
        var option = BuildEuropeanOption(s.GetProperty("strike").GetDecimal(), expiry, s.GetProperty("call_put").GetString()!);
        var parameters = new HestonParameters(s.GetProperty("v0").GetDouble(), s.GetProperty("kappa").GetDouble(),
            s.GetProperty("theta").GetDouble(), s.GetProperty("sigma_v").GetDouble(), s.GetProperty("rho").GetDouble());

        // Declared before execution: four standard errors plus a separate Euler bias
        // allowance of 10 cents (regular) / 20 cents (non-Feller) on a $100 spot.
        // These are finite-step qualification bounds, not analytic precision claims.
        const int paths = 50_000;
        const int steps = 256;
        ulong seed = 2026102700UL + (ulong)id;
        double biasAllowance = s.GetProperty("family").GetString() == "regular" ? 0.10d : 0.20d;
        var result = new SimpleHestonMcPricer(parameters, paths, steps, new Pcg64RandomSource(seed)).Price(option, market);
        Assert.True(result.IsSuccess);
        double actual = (double)result.Price.Amount;
        double expected = s.GetProperty("price").GetDouble();
        double se = (double)result.GetDiagnostics()["standardError"]!;
        Assert.True(double.IsFinite(se) && se > 0d);
        double bound = 4d * se + biasAllowance;
        output.WriteLine($"id={id} seed={seed} paths={paths} steps={steps} actual={actual:R} oracle={expected:R} SE={se:R} biasAllowance={biasAllowance:R} bound={bound:R}");
        AssertWithin(actual, expected, bound, $"QuantLib Heston scenario {id}: independent analytic characteristic-function oracle");
    }
}
