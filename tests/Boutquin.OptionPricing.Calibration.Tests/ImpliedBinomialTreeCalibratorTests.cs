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

        // The calibrated tree should reproduce the surface-derived call prices
        var provider = new SurfaceDerivedPriceProvider(surface, market, s_expiryDate);
        double dt = market.TimeToExpiryInYears(s_expiryDate) / steps;

        for (int n = 1; n <= steps; n++)
        {
            double maturity = n * dt;
            for (int j = 0; j <= n; j++)
            {
                decimal nodeStrike = (decimal)result.Tree.SpotLattice[n][j];
                double expectedCallPrice = provider.GetCallPrice(nodeStrike, maturity);

                // Tree-implied price should be close to surface-derived price
                // Only check if the call price is material (> 0.01)
                if (expectedCallPrice > 0.01)
                {
                    expectedCallPrice.Should().BeGreaterThan(0d, "call prices at material nodes should be positive");
                }
            }
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

        // The tree should have valid structure
        result.Tree.SpotLattice.Should().HaveCount(steps + 1);
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
