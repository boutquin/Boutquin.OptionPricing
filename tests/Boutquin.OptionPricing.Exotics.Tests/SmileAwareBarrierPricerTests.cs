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
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Exotics.Barriers;
using Boutquin.OptionPricing.Trees.Binomial;
using FluentAssertions;

namespace Boutquin.OptionPricing.Exotics.Tests;

/// <summary>
/// Tests for <see cref="SmileAwareBarrierPricer"/> — barrier pricing on calibrated implied trees.
/// </summary>
public sealed class SmileAwareBarrierPricerTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 1);
    private static readonly DateOnly s_expiryDate = new(2026, 7, 1);

    [Fact]
    public void SmileAwarePrice_ShouldDifferFromFlatVolPrice()
    {
        // With a smile, the smile-aware price should differ from flat-vol price
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateSmiledSurface();
        int steps = 50;

        var calibration = new ImpliedBinomialTreeCalibrator().Calibrate(
            new ImpliedVolatilityTreeCalibrationInput(surface, steps, market));

        var option = new BarrierOption(CallPut.Call, 100m, 120m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        var smileResult = new SmileAwareBarrierPricer().Price(option, market, calibration);

        // Flat-vol price for comparison
        var flatTree = new CoxRossRubinsteinTreeBuilder().Build(
            TreeModelInput.FromMarketData(market, 100m, s_expiryDate, steps));
        var flatResult = new BarrierTreePricer().Price(option, market, flatTree);

        smileResult.IsSuccess.Should().BeTrue();
        // With a smile, prices should differ (proves smile effect)
        Math.Abs(smileResult.Price - flatResult.Price).Should().BeGreaterThan(0m);
    }

    [Fact]
    public void SmileAwarePrice_BarrierParity_ShouldHold()
    {
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20);
        int steps = 50;

        var calibration = new ImpliedBinomialTreeCalibrator().Calibrate(
            new ImpliedVolatilityTreeCalibrationInput(surface, steps, market));

        var knockOut = new BarrierOption(CallPut.Call, 100m, 130m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        var knockIn = new BarrierOption(CallPut.Call, 100m, 130m, BarrierDirection.Up, BarrierKnockType.In, s_expiryDate);

        var pricer = new SmileAwareBarrierPricer();
        decimal outPrice = pricer.Price(knockOut, market, calibration).Price;
        decimal inPrice = pricer.Price(knockIn, market, calibration).Price;

        // Price vanilla on the same tree for comparison
        var vanillaOption = new EuropeanOption(CallPut.Call, 100m, s_expiryDate);
        var vanillaTree = calibration.Tree;
        decimal vanillaPrice = new Trees.Pricing.EuropeanTreePricer().Price(vanillaOption, market, vanillaTree).Price;

        (outPrice + inPrice).Should().BeApproximately(vanillaPrice, 0.10m);
    }

    [Fact]
    public void SmileAwarePrice_SuccessfulCalibration_ShouldReturnSuccess()
    {
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20);
        int steps = 20;

        var calibration = new ImpliedBinomialTreeCalibrator().Calibrate(
            new ImpliedVolatilityTreeCalibrationInput(surface, steps, market));

        var option = new BarrierOption(CallPut.Put, 100m, 80m, BarrierDirection.Down, BarrierKnockType.Out, s_expiryDate);
        var result = new SmileAwareBarrierPricer().Price(option, market, calibration);

        result.IsSuccess.Should().BeTrue();
        result.Price.Should().BeGreaterThanOrEqualTo(0m);
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
        var points = new List<SurfacePoint>
        {
            new(0.5, 80m, vol),
            new(0.5, 90m, vol),
            new(0.5, 100m, vol),
            new(0.5, 110m, vol),
            new(0.5, 120m, vol)
        };
        return new VolatilitySurface(new[] { new SmileSlice(0.5, points) });
    }

    private static VolatilitySurface CreateSmiledSurface()
    {
        var points = new List<SurfacePoint>
        {
            new(0.5, 80m, 0.30),
            new(0.5, 90m, 0.24),
            new(0.5, 100m, 0.20),
            new(0.5, 110m, 0.22),
            new(0.5, 120m, 0.28)
        };
        return new VolatilitySurface(new[] { new SmileSlice(0.5, points) });
    }
}
