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

using Boutquin.Curves.Abstractions.Identifiers;
using Boutquin.Curves.Core.Discounting;
using Boutquin.Domain.Enumerations;
using Boutquin.Domain.Extensions;
using Boutquin.OptionPricing.Calibration.ImpliedTrees;
using Boutquin.OptionPricing.Calibration.Smile;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Calibration.Tests;

/// <summary>
/// Tests for <see cref="ImpliedBinomialTreeCalibrator"/> — Rubinstein-style forward induction.
/// </summary>
public sealed class ImpliedBinomialTreeCalibratorTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 1);
    private static readonly DateOnly s_expiryDate = new(2026, 7, 1);

    [Fact]
    public void Calibrate_FlatSurface_ShouldReproduceSurfaceDerivedCallPrices()
    {
        // With a flat surface, the calibrated tree should reproduce BS call prices
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20);
        int steps = 10;

        var input = new ImpliedVolatilityTreeCalibrationInput(surface, steps, market);
        var result = new ImpliedBinomialTreeCalibrator().Calibrate(input);

        result.IsSuccess.Should().BeTrue();
        result.Tree.Steps.Should().Be(steps);

        // Fixed near-money strikes avoid the finite lattice's unrepresented tails.
        // The AD-weighted payoff is the actual tree premium, not a quote-provider tautology.
        var provider = new SurfaceDerivedPriceProvider(surface, market, s_expiryDate);
        double maturity = result.Tree.Steps * result.Tree.DeltaTime;
        foreach (decimal strike in new[] { 90m, 100m, 110m })
        {
            double expected = provider.GetCallPrice(strike, maturity);
            double actual = result.ArrowDebreuPrices[^1].Select((ad, j) =>
                ad * Math.Max(result.Tree.TerminalSpots[j] - (double)strike, 0d)).Sum();
            actual.Should().BeApproximately(expected, 0.5, "near-money premiums have finite-tree discretization error");
        }
    }

    [Fact]
    public void Calibrate_ArrowDebreuPrices_ShouldSumToDiscountFactor()
    {
        // Sum of AD prices at each step should equal the discount factor to that step
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20);
        int steps = 10;

        var input = new ImpliedVolatilityTreeCalibrationInput(surface, steps, market);
        var result = new ImpliedBinomialTreeCalibrator().Calibrate(input);

        result.IsSuccess.Should().BeTrue();
        double r = market.RiskFreeRate(s_expiryDate);
        double dt = result.Tree.DeltaTime;

        for (int n = 0; n < result.ArrowDebreuPrices.Count; n++)
        {
            double adSum = result.ArrowDebreuPrices[n].Sum();
            double expectedDiscount = Math.Exp(-r * n * dt);
            adSum.Should().BeApproximately(expectedDiscount, 1e-6,
                $"AD prices at step {n} should sum to discount factor");
        }
    }

    [Fact]
    public void Calibrate_ImpliedProbabilities_ShouldBeWithinBounds()
    {
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20);
        int steps = 10;

        var input = new ImpliedVolatilityTreeCalibrationInput(surface, steps, market);
        var result = new ImpliedBinomialTreeCalibrator().Calibrate(input);

        result.IsSuccess.Should().BeTrue();

        result.Tree.ProbabilityLattice.Should().NotBeNull();
        foreach (IReadOnlyList<double> row in result.Tree.ProbabilityLattice!)
        {
            row.Should().OnlyContain(p => double.IsFinite(p) && p > 0d && p < 1d);
        }
    }

    [Fact]
    public void Calibrate_LocalVolatilities_ShouldBePositive()
    {
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20);
        int steps = 10;

        var input = new ImpliedVolatilityTreeCalibrationInput(surface, steps, market);
        var result = new ImpliedBinomialTreeCalibrator().Calibrate(input);

        result.IsSuccess.Should().BeTrue();

        foreach (IReadOnlyList<double> stepVols in result.LocalVolatilities)
        {
            foreach (double vol in stepVols)
            {
                vol.Should().BeGreaterThan(0d, "local volatility should be positive");
            }
        }
    }

    [Fact]
    public void Calibrate_NoArbitrageReversions_ShouldBeReportedAsDiagnostic()
    {
        // F-L6-02: the engine reverts no-arbitrage-violating nodes to the constant-volatility
        // seed, but that degradation signal was discarded — every calibration reported
        // unconditional Success. The reversion tally must now surface as a diagnostic and a
        // flat surface (which never reverts) must stay a clean Success.
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20);

        var input = new ImpliedVolatilityTreeCalibrationInput(surface, 20, market);
        var result = new ImpliedBinomialTreeCalibrator().Calibrate(input);

        var diagnostics = result.Report.GetDiagnostics();
        diagnostics.Should().ContainKey("nodeReversions",
            "the engine's no-arbitrage reversion tally must reach the calibration report");
        diagnostics["nodeReversions"].Should().BeOfType<int>()
            .Which.Should().BeGreaterThanOrEqualTo(0);

        // A flat surface never violates the bracket, so it converges cleanly with no reversions.
        ((int)diagnostics["nodeReversions"]!).Should().Be(0);
        result.Report.Status.Should().Be(Domain.Enums.CalculationStatus.Success);
        result.IsSuccess.Should().BeTrue();
    }

    private static OptionMarketData CreateMarket(decimal spot, double vol, double rate)
    {
        return new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(spot),
            vol,
            new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, Currency.USD, rate));
    }

    private static VolatilitySurface CreateFlatSurface(double vol)
    {
        // Create multiple slices spanning the term
        var points1 = new List<SurfacePoint>
        {
            new(0.25, 80m, vol),
            new(0.25, 90m, vol),
            new(0.25, 100m, vol),
            new(0.25, 110m, vol),
            new(0.25, 120m, vol)
        };
        var points2 = new List<SurfacePoint>
        {
            new(0.5, 80m, vol),
            new(0.5, 90m, vol),
            new(0.5, 100m, vol),
            new(0.5, 110m, vol),
            new(0.5, 120m, vol)
        };
        return new VolatilitySurface(new[]
        {
            new SmileSlice(0.25, points1),
            new SmileSlice(0.5, points2)
        });
    }
}
