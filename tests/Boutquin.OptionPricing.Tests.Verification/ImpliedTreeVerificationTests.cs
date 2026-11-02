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
using Boutquin.OptionPricing.Calibration.ImpliedTrees;
using Boutquin.OptionPricing.Calibration.Smile;

namespace Boutquin.OptionPricing.Tests.Verification;

/// <summary>
/// Cross-language verification for the implied-tree calibrators
/// (ImpliedBinomialTreeCalibrator and ImpliedVolatilityTreeCalibrator). Both build a
/// genuine Derman-Kani implied tree by forward induction; rather than replicate that
/// node-by-node solve in Python, the verifier asserts the analytic *correctness
/// properties* the Python reference computes: the terminal Arrow-Debreu prices sum to
/// the discount factor, the terminal forward equals S0·e^{-qT}, and the tree reprices
/// near-the-money Black-Scholes calls to finite-tree accuracy.
/// </summary>
public sealed class ImpliedTreeVerificationTests : CrossLanguageVerificationBase
{
    // Finite-tree repricing accuracy for the near-the-money region across the scenario
    // step counts (10, 20). The constant-vol lattice of the same depth has comparable error.
    private const double RepricingTolerance = 0.6;
    private const double ForwardTolerance = 0.3;

    public static IEnumerable<object[]> Scenarios()
    {
        using var doc = LoadVector("implied_tree");
        var index = 0;
        foreach (var entry in doc.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            yield return new object[] { index++, JsonSerializer.Serialize(entry) };
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void RubinsteinCalibrator_SatisfiesReferenceProperties(int index, string scenarioJson)
    {
        var (input, expected) = ParseScenario(scenarioJson);
        var result = new ImpliedBinomialTreeCalibrator().Calibrate(input);
        AssertProperties(index, result, expected, "rubinstein");
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void DermanKaniCalibrator_SatisfiesReferenceProperties(int index, string scenarioJson)
    {
        var (input, expected) = ParseScenario(scenarioJson);
        var result = new ImpliedVolatilityTreeCalibrator().Calibrate(input);
        AssertProperties(index, result, expected, "derman-kani");
    }

    private (ImpliedVolatilityTreeCalibrationInput Input, JsonElement Expected) ParseScenario(string scenarioJson)
    {
        var s = JsonDocument.Parse(scenarioJson).RootElement;
        var valuationDate = ParseDate(s.GetProperty("valuation_date").GetString()!);
        var spot = new decimal(s.GetProperty("spot").GetDouble());
        var sigma0 = s.GetProperty("sigma0").GetDouble();
        var r = s.GetProperty("r").GetDouble();
        var q = s.GetProperty("q").GetDouble();
        var steps = s.GetProperty("steps").GetInt32();

        var market = BuildMarket(valuationDate, spot, sigma0, r, q);

        var surfacePoints = s.GetProperty("surface_points").EnumerateArray()
            .Select(p => new SurfacePoint(
                p.GetProperty("maturity").GetDouble(),
                p.GetProperty("strike").GetDecimal(),
                p.GetProperty("vol").GetDouble()))
            .ToList();
        var surface = new VolatilitySurfaceBuilder().Build(surfacePoints);

        var input = new ImpliedVolatilityTreeCalibrationInput(surface, steps, market);
        return (input, s.GetProperty("expected"));
    }

    private static void AssertProperties(int index, ImpliedTreeResult result, JsonElement expected, string label)
    {
        Assert.True(result.IsSuccess, $"implied_tree[{index}/{label}] did not succeed");

        int n = result.Tree.Steps;
        IReadOnlyList<double> terminalSpots = result.Tree.SpotLattice[n];
        IReadOnlyList<double> terminalAd = result.ArrowDebreuPrices[n];

        double mass = 0d;
        double forward = 0d;
        for (int j = 0; j < terminalSpots.Count; j++)
        {
            Assert.True(terminalAd[j] >= -1e-12, $"implied_tree[{index}/{label}] negative AD price");
            mass += terminalAd[j];
            forward += terminalAd[j] * terminalSpots[j];
        }

        AssertWithin(mass, expected.GetProperty("discount_factor").GetDouble(), 1e-6,
            $"implied_tree[{index}/{label}] AD mass = discount factor");
        AssertWithin(forward, expected.GetProperty("forward").GetDouble(), ForwardTolerance,
            $"implied_tree[{index}/{label}] terminal forward");

        foreach (var quote in expected.GetProperty("near_money_calls").EnumerateArray())
        {
            double strike = quote.GetProperty("strike").GetDouble();
            double treeCall = 0d;
            for (int j = 0; j < terminalSpots.Count; j++)
            {
                treeCall += terminalAd[j] * Math.Max(terminalSpots[j] - strike, 0d);
            }

            AssertWithin(treeCall, quote.GetProperty("call").GetDouble(), RepricingTolerance,
                $"implied_tree[{index}/{label}] reprice K={strike}");
        }
    }
}
