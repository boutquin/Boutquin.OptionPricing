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
/// (ImpliedBinomialTreeCalibrator and ImpliedVolatilityTreeCalibrator). The
/// current C# implementations both build a CRR-structured lattice with
/// clamped probabilities, so the Python reference replicates that actual
/// behavior and asserts the calibrated tree, AD prices, and local vols
/// match at <see cref="CrossLanguageVerificationBase.PrecisionNumeric"/>.
/// </summary>
public sealed class ImpliedTreeVerificationTests : CrossLanguageVerificationBase
{
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
    public void RubinsteinCalibrator_MatchesPythonReference(int index, string scenarioJson)
    {
        var (input, expected) = ParseScenario(scenarioJson);
        var result = new ImpliedBinomialTreeCalibrator().Calibrate(input);
        AssertTreeMatches(index, result, expected, "rubinstein");
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void DermanKaniCalibrator_MatchesPythonReference(int index, string scenarioJson)
    {
        var (input, expected) = ParseScenario(scenarioJson);
        var result = new ImpliedVolatilityTreeCalibrator().Calibrate(input);
        AssertTreeMatches(index, result, expected, "derman-kani");
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

    private void AssertTreeMatches(int index, ImpliedTreeResult result, JsonElement expected, string label)
    {
        Assert.True(result.IsSuccess, $"implied_tree[{index}/{label}] did not succeed");

        AssertWithin(result.Tree.UpFactor, expected.GetProperty("u").GetDouble(), PrecisionNumeric,
            $"implied_tree[{index}/{label}] u");
        AssertWithin(result.Tree.DownFactor, expected.GetProperty("d").GetDouble(), PrecisionNumeric,
            $"implied_tree[{index}/{label}] d");
        AssertWithin(result.Tree.RiskNeutralProbability, expected.GetProperty("p").GetDouble(),
            PrecisionNumeric, $"implied_tree[{index}/{label}] p");
        AssertWithin(result.Tree.DeltaTime, expected.GetProperty("dt").GetDouble(), PrecisionNumeric,
            $"implied_tree[{index}/{label}] dt");

        AssertLatticeEquals(result.Tree.SpotLattice, expected.GetProperty("spot_lattice"),
            $"implied_tree[{index}/{label}] spot");
        AssertLatticeEquals(result.ArrowDebreuPrices, expected.GetProperty("arrow_debreu_prices"),
            $"implied_tree[{index}/{label}] AD");
        AssertLatticeEquals(result.LocalVolatilities, expected.GetProperty("local_volatilities"),
            $"implied_tree[{index}/{label}] σ_local");
    }

    private static void AssertLatticeEquals(
        IReadOnlyList<IReadOnlyList<double>> actual,
        JsonElement expected,
        string label)
    {
        var expectedLayers = expected.EnumerateArray().ToArray();
        Assert.Equal(expectedLayers.Length, actual.Count);
        for (int step = 0; step < actual.Count; step++)
        {
            var expectedLayer = expectedLayers[step].EnumerateArray().Select(e => e.GetDouble()).ToArray();
            Assert.Equal(expectedLayer.Length, actual[step].Count);
            for (int j = 0; j < expectedLayer.Length; j++)
            {
                AssertWithin(actual[step][j], expectedLayer[j], PrecisionNumeric,
                    $"{label} [{step}][{j}]");
            }
        }
    }
}
