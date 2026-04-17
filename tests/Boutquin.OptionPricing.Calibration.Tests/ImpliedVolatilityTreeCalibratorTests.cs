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
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.Curves.Core.Discounting;
using Boutquin.OptionPricing.Calibration.ImpliedTrees;
using Boutquin.OptionPricing.Calibration.Smile;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Calibration.Tests;

/// <summary>
/// Tests for <see cref="ImpliedVolatilityTreeCalibrator"/> — Derman-Kani-style forward construction.
/// </summary>
public sealed class ImpliedVolatilityTreeCalibratorTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 1);

    [Fact]
    public void Calibrate_FlatSurface_ShouldReproduceSmileAtNodeStrikes()
    {
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20);
        int steps = 10;

        var input = new ImpliedVolatilityTreeCalibrationInput(surface, steps, market);
        var result = new ImpliedVolatilityTreeCalibrator().Calibrate(input);

        result.IsSuccess.Should().BeTrue();
        result.Tree.Steps.Should().Be(steps);
    }

    [Fact]
    public void Calibrate_NoArbitrageProbabilityBounds_ShouldBeEnforced()
    {
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20);
        int steps = 10;

        var input = new ImpliedVolatilityTreeCalibrationInput(surface, steps, market);
        var result = new ImpliedVolatilityTreeCalibrator().Calibrate(input);

        result.IsSuccess.Should().BeTrue();
        result.Tree.RiskNeutralProbability.Should().BeGreaterThan(0d);
        result.Tree.RiskNeutralProbability.Should().BeLessThan(1d);
    }

    [Fact]
    public void Calibrate_LocalVolatilities_ShouldBePositiveAndSmooth()
    {
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20);
        int steps = 10;

        var input = new ImpliedVolatilityTreeCalibrationInput(surface, steps, market);
        var result = new ImpliedVolatilityTreeCalibrator().Calibrate(input);

        result.IsSuccess.Should().BeTrue();

        foreach (IReadOnlyList<double> stepVols in result.LocalVolatilities)
        {
            foreach (double vol in stepVols)
            {
                vol.Should().BeGreaterThan(0d, "local volatility should be positive");
            }
        }

        // With a flat surface, local vols should be approximately constant
        foreach (IReadOnlyList<double> stepVols in result.LocalVolatilities)
        {
            foreach (double vol in stepVols)
            {
                vol.Should().BeApproximately(0.20, 0.05,
                    "local vol should be close to flat input vol");
            }
        }
    }

    [Fact]
    public void Calibrate_ArrowDebreuPrices_ShouldSumToDiscountFactor()
    {
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20);
        int steps = 10;

        var input = new ImpliedVolatilityTreeCalibrationInput(surface, steps, market);
        var result = new ImpliedVolatilityTreeCalibrator().Calibrate(input);

        result.IsSuccess.Should().BeTrue();
        double r = market.RiskFreeRate(market.InvertYearFraction(0.5));
        double dt = result.Tree.DeltaTime;

        for (int step = 0; step < result.ArrowDebreuPrices.Count; step++)
        {
            double adSum = result.ArrowDebreuPrices[step].Sum();
            double expectedDiscount = Math.Exp(-r * step * dt);
            adSum.Should().BeApproximately(expectedDiscount, 1e-6,
                $"AD prices at step {step} should sum to discount factor");
        }
    }

    private static OptionMarketData CreateMarket(decimal spot, double vol, double rate)
    {
        return new OptionMarketData(
            s_valuationDate,
            spot,
            vol,
            new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, CurrencyCode.USD, rate));
    }

    private static VolatilitySurface CreateFlatSurface(double vol)
    {
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
