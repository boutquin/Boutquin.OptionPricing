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
using Boutquin.OptionPricing.Calibration.Smile;

namespace Boutquin.OptionPricing.Tests.Verification;

/// <summary>
/// Cross-language verification for the Smile module: VolatilitySurfaceBuilder
/// structural ordering, VolatilitySurface.GetVol at grid coordinates and in
/// two-node linear reductions, and QuoteArbitrageValidator's arbitrage
/// findings. Full bivariate-interpolation comparison against scipy is not
/// attempted because the C# side uses Boutquin.Numerics-specific schemes
/// (arXiv:2402.01324 monotone-cubic Hermite + arXiv:2507.05083 modified
/// not-a-knot spline) — we pin down only the paths whose contract is
/// independent of those choices.
/// </summary>
public sealed class SmileVerificationTests : CrossLanguageVerificationBase
{
    private static SurfacePoint ParsePoint(JsonElement e) =>
        new(e.GetProperty("maturity").GetDouble(),
            e.GetProperty("strike").GetDecimal(),
            e.GetProperty("vol").GetDouble());

    private static List<SurfacePoint> ParsePoints(JsonElement array) =>
        array.EnumerateArray().Select(ParsePoint).ToList();

    public static IEnumerable<object[]> StructuralScenarios()
    {
        using var doc = LoadVector("smile");
        var index = 0;
        foreach (var entry in doc.RootElement.GetProperty("structural").EnumerateArray())
        {
            yield return new object[] { index++, JsonSerializer.Serialize(entry) };
        }
    }

    public static IEnumerable<object[]> GridReadScenarios()
    {
        using var doc = LoadVector("smile");
        var index = 0;
        foreach (var entry in doc.RootElement.GetProperty("grid_reads").EnumerateArray())
        {
            yield return new object[] { index++, JsonSerializer.Serialize(entry) };
        }
    }

    public static IEnumerable<object[]> TwoNodeScenarios()
    {
        using var doc = LoadVector("smile");
        var index = 0;
        foreach (var entry in doc.RootElement.GetProperty("two_node_linear").EnumerateArray())
        {
            yield return new object[] { index++, JsonSerializer.Serialize(entry) };
        }
    }

    public static IEnumerable<object[]> ArbitrageScenarios()
    {
        using var doc = LoadVector("smile");
        var index = 0;
        foreach (var entry in doc.RootElement.GetProperty("arbitrage").EnumerateArray())
        {
            yield return new object[] { index++, JsonSerializer.Serialize(entry) };
        }
    }

    [Theory]
    [MemberData(nameof(StructuralScenarios))]
    public void SurfaceBuilder_ProducesMaturityOrderedStrikeAscendingSlices(int index, string scenarioJson)
    {
        var s = JsonDocument.Parse(scenarioJson).RootElement;
        var name = s.GetProperty("name").GetString()!;
        var points = ParsePoints(s.GetProperty("points"));

        var surface = new VolatilitySurfaceBuilder().Build(points);
        var expectedSlices = s.GetProperty("expected_slices").EnumerateArray().ToArray();

        Assert.Equal(expectedSlices.Length, surface.Slices.Count);

        for (int i = 0; i < expectedSlices.Length; i++)
        {
            var expectedMaturity = expectedSlices[i].GetProperty("maturity").GetDouble();
            var expectedStrikes = expectedSlices[i].GetProperty("strikes")
                .EnumerateArray().Select(e => e.GetDecimal()).ToArray();
            var expectedVols = expectedSlices[i].GetProperty("vols")
                .EnumerateArray().Select(e => e.GetDouble()).ToArray();

            var slice = surface.Slices[i];
            AssertWithin(slice.MaturityInYears, expectedMaturity, PrecisionExact,
                $"structural[{index}/{name}] slice[{i}] maturity");
            Assert.Equal(expectedStrikes.Length, slice.Points.Count);
            for (int j = 0; j < expectedStrikes.Length; j++)
            {
                Assert.Equal(expectedStrikes[j], slice.Points[j].Strike);
                AssertWithin(slice.Points[j].ImpliedVolatility, expectedVols[j],
                    PrecisionExact, $"structural[{index}/{name}] slice[{i}] vol[{j}]");
            }
        }
    }

    [Theory]
    [MemberData(nameof(GridReadScenarios))]
    public void SurfaceGetVol_AtGridCoordinate_ReturnsStoredVol(int index, string scenarioJson)
    {
        var s = JsonDocument.Parse(scenarioJson).RootElement;
        var points = ParsePoints(s.GetProperty("grid"));
        var surface = new VolatilitySurfaceBuilder().Build(points);

        var q = 0;
        foreach (var query in s.GetProperty("queries").EnumerateArray())
        {
            var strike = query.GetProperty("strike").GetDecimal();
            var maturity = query.GetProperty("maturity").GetDouble();
            var expected = query.GetProperty("expected_vol").GetDouble();

            var actual = surface.GetVol(strike, maturity);

            AssertWithin(actual, expected, PrecisionNumeric,
                $"grid[{index}] query[{q++}] (K={strike} T={maturity})");
        }
    }

    [Theory]
    [MemberData(nameof(TwoNodeScenarios))]
    public void SurfaceGetVol_TwoNodeLinearReduction_MatchesReference(int index, string scenarioJson)
    {
        var s = JsonDocument.Parse(scenarioJson).RootElement;
        var name = s.GetProperty("name").GetString()!;
        var points = ParsePoints(s.GetProperty("grid"));
        var surface = new VolatilitySurfaceBuilder().Build(points);

        var q = 0;
        foreach (var query in s.GetProperty("queries").EnumerateArray())
        {
            var strike = query.GetProperty("strike").GetDecimal();
            var maturity = query.GetProperty("maturity").GetDouble();
            var expected = query.GetProperty("expected_vol").GetDouble();

            var actual = surface.GetVol(strike, maturity);

            AssertWithin(actual, expected, PrecisionNumeric,
                $"two_node[{index}/{name}] query[{q++}] (K={strike} T={maturity})");
        }
    }

    [Theory]
    [MemberData(nameof(ArbitrageScenarios))]
    public void ArbitrageValidator_FlagsExpectedOutcome(int index, string scenarioJson)
    {
        var s = JsonDocument.Parse(scenarioJson).RootElement;
        var name = s.GetProperty("name").GetString()!;
        var points = ParsePoints(s.GetProperty("grid"));
        var expectedValid = s.GetProperty("expected_valid").GetBoolean();
        var minFindings = s.GetProperty("min_findings").GetInt32();

        var surface = new VolatilitySurfaceBuilder().Build(points);
        var result = new QuoteArbitrageValidator().Validate(surface);

        Assert.Equal(expectedValid, result.IsValid);
        Assert.True(result.Findings.Count >= minFindings,
            $"arbitrage[{index}/{name}] expected ≥{minFindings} findings, got {result.Findings.Count}");
    }
}
