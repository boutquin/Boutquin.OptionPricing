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
    public void CalibratedSmileTree_RepricesAtmEuropeanCall_ToFiniteTreeAccuracy()
    {
        // The Derman-Kani implied tree must reprice its own calibration surface through the
        // production backward-induction pricer. At the ATM node strike (K=100) the surface vol
        // is exactly 0.20, so the BS reference needs no interpolation. The per-node martingale
        // probabilities are what make the tree reprice; rolling back with the surface-AVERAGED
        // probability collapses the smile content and mis-prices ATM by > 1.0 (F-L6-01).
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateSmiledSurface();
        int steps = 50;

        var calibration = new ImpliedBinomialTreeCalibrator().Calibrate(
            new ImpliedVolatilityTreeCalibrationInput(surface, steps, market));
        calibration.IsSuccess.Should().BeTrue();

        // Reprice the ATM European call through the production tree pricer on the calibrated tree.
        var atmCall = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiryDate);
        decimal treeCall = new Trees.Pricing.EuropeanTreePricer().Price(atmCall, market, calibration.Tree).Price.Amount;

        // Black-Scholes reference at K=100, sigma=0.20 (the surface's ATM node vol), T=0.5, r=0.05, q=0.
        double bsAtm = BlackScholesCall(100d, 100d, 0.05d, 0d, 0.20d, 0.5d);

        // Finite-tree accuracy near the money. With per-node p the error is ~0.05; with the flat
        // average it is ~1.4 (≈ 25× worse), so 0.5 cleanly separates the two regimes.
        ((double)treeCall).Should().BeApproximately(bsAtm, 0.5d,
            "the calibrated implied tree must reprice its own ATM surface call to finite-tree accuracy");
    }

    private static double BlackScholesCall(double spot, double strike, double rate, double dividendYield, double vol, double t)
    {
        double sqrtT = Math.Sqrt(t);
        double d1 = (Math.Log(spot / strike) + ((rate - dividendYield + (0.5d * vol * vol)) * t)) / (vol * sqrtT);
        double d2 = d1 - (vol * sqrtT);
        return (spot * Math.Exp(-dividendYield * t) * Cnd(d1)) - (strike * Math.Exp(-rate * t) * Cnd(d2));
    }

    private static double Cnd(double x)
    {
        // Standard normal CDF via the error function complement (Abramowitz-Stegun 7.1.26-grade);
        // sufficient precision for a 0.5-wide repricing assertion.
        return 0.5d * Erfc(-x / Math.Sqrt(2d));
    }

    private static double Erfc(double x)
    {
        double z = Math.Abs(x);
        double tt = 1d / (1d + (0.5d * z));
        double poly = -1.26551223d
            + (tt * 1.00002368d)
            + (tt * tt * 0.37409196d)
            + (tt * tt * tt * 0.09678418d)
            + (tt * tt * tt * tt * -0.18628806d)
            + (tt * tt * tt * tt * tt * 0.27886807d)
            + (tt * tt * tt * tt * tt * tt * -1.13520398d)
            + (tt * tt * tt * tt * tt * tt * tt * 1.48851587d)
            + (tt * tt * tt * tt * tt * tt * tt * tt * -0.82215223d)
            + (tt * tt * tt * tt * tt * tt * tt * tt * tt * 0.17087277d);
        double r = tt * Math.Exp((-z * z) + poly);
        return x >= 0d ? r : 2d - r;
    }

    [Fact]
    public void SmileAwarePrice_ShouldDifferFromFlatVolPrice()
    {
        // With a smile, the smile-aware price should differ from flat-vol price
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateSmiledSurface();
        int steps = 50;

        var calibration = new ImpliedBinomialTreeCalibrator().Calibrate(
            new ImpliedVolatilityTreeCalibrationInput(surface, steps, market));

        var option = new BarrierOption(CallPut.Call, 100m, Currency.USD, 120m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        var smileResult = new SmileAwareBarrierPricer().Price(option, market, calibration);

        // Flat-vol price for comparison
        var flatTree = new CoxRossRubinsteinTreeBuilder().Build(
            TreeModelInput.FromMarketData(market, 100m, s_expiryDate, steps));
        var flatResult = new BarrierTreePricer().Price(option, market, flatTree);

        smileResult.IsSuccess.Should().BeTrue();
        // With a smile, prices should differ (proves smile effect)
        Math.Abs(smileResult.Price.Amount - flatResult.Price.Amount).Should().BeGreaterThan(0m);
    }

    [Fact]
    public void SmileAwarePrice_BarrierParity_ShouldHold()
    {
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20);
        int steps = 50;

        var calibration = new ImpliedBinomialTreeCalibrator().Calibrate(
            new ImpliedVolatilityTreeCalibrationInput(surface, steps, market));

        var knockOut = new BarrierOption(CallPut.Call, 100m, Currency.USD, 130m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        var knockIn = new BarrierOption(CallPut.Call, 100m, Currency.USD, 130m, BarrierDirection.Up, BarrierKnockType.In, s_expiryDate);

        var pricer = new SmileAwareBarrierPricer();
        decimal outPrice = pricer.Price(knockOut, market, calibration).Price.Amount;
        decimal inPrice = pricer.Price(knockIn, market, calibration).Price.Amount;

        // Price vanilla on the same tree for comparison
        var vanillaOption = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiryDate);
        var vanillaTree = calibration.Tree;
        decimal vanillaPrice = new Trees.Pricing.EuropeanTreePricer().Price(vanillaOption, market, vanillaTree).Price.Amount;

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

        var option = new BarrierOption(CallPut.Put, 100m, Currency.USD, 80m, BarrierDirection.Down, BarrierKnockType.Out, s_expiryDate);
        var result = new SmileAwareBarrierPricer().Price(option, market, calibration);

        result.IsSuccess.Should().BeTrue();
        result.Price.Amount.Should().BeGreaterThanOrEqualTo(0m);
    }

    // ── RED test for F-L23-01 (016): knockInActivated missing in knock-in breached-at-spot branch ──
    [Fact]
    public void SmileAwarePrice_KnockIn_AlreadyBreachedAtSpot_DiagnosticsContainsKnockInActivated()
    {
        // spot=100, Up-In call, barrier=90 → spot >= barrier at t0 → already breached → knock-in activated.
        // Expected: diagnostics must contain BOTH "barrierBreachedAtSpot" AND "knockInActivated".
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20);
        int steps = 10;

        var calibration = new ImpliedBinomialTreeCalibrator().Calibrate(
            new ImpliedVolatilityTreeCalibrationInput(surface, steps, market));
        calibration.IsSuccess.Should().BeTrue();

        var option = new BarrierOption(CallPut.Call, 100m, Currency.USD, 90m, BarrierDirection.Up, BarrierKnockType.In, s_expiryDate);
        var result = new SmileAwareBarrierPricer().Price(option, market, calibration);

        result.IsSuccess.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("barrierBreachedAtSpot");
        result.Diagnostics.Should().ContainKey("knockInActivated",
            "SmileAwareBarrierPricer must match the contract of BarrierTreePricer for the knock-in breached-at-spot branch");
    }

    // ── Tests for F-L4-02 (014) and F-L4-03 (015) ──
    [Fact]
    public void SmileAwarePrice_FailedCalibration_ReturnsUnsupported()
    {
        // Pass an ImpliedTreeResult with IsSuccess=false to exercise the failed-calibration branch.
        // Predicate: result.IsSuccess must be false when calibration.IsSuccess is false.
        var market = CreateMarket(100m, 0.20, 0.05);
        var option = new BarrierOption(CallPut.Call, 100m, Currency.USD, 120m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);

        // Construct a failed calibration: CalibrationReport.IsSuccess == false when Status is neither Success nor ConvergedWithWarnings
        var failedReport = new Boutquin.OptionPricing.Calibration.ImpliedTrees.CalibrationReport(
            "test",
            Boutquin.OptionPricing.Domain.Enums.CalculationStatus.FailedToConverge,
            new Dictionary<string, object?>());

        // BinomialTree is not accessed when calibration fails — pass null tree via ImpliedTreeResult ctor
        // ImpliedTreeResult is a record: ImpliedTreeResult(BinomialTree, IReadOnlyList..., IReadOnlyList..., CalibrationReport)
        var failedCalibration = new Boutquin.OptionPricing.Calibration.ImpliedTrees.ImpliedTreeResult(
            null!,
            Array.Empty<IReadOnlyList<double>>(),
            Array.Empty<IReadOnlyList<double>>(),
            failedReport);

        failedCalibration.IsSuccess.Should().BeFalse();

        var result = new SmileAwareBarrierPricer().Price(option, market, failedCalibration);

        result.IsSuccess.Should().BeFalse(
            "Price must return Unsupported when calibration.IsSuccess is false");
    }

    [Fact]
    public void SmileAwarePrice_UpOutKnockOut_AlreadyBreachedAtSpot_ReturnsZero()
    {
        // spot=100, Up-Out call, barrier=90 → spot >= barrier → breached at t0 → knock-out returns 0.
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20);
        int steps = 10;

        var calibration = new ImpliedBinomialTreeCalibrator().Calibrate(
            new ImpliedVolatilityTreeCalibrationInput(surface, steps, market));
        calibration.IsSuccess.Should().BeTrue();

        var option = new BarrierOption(CallPut.Call, 100m, Currency.USD, 90m, BarrierDirection.Up, BarrierKnockType.Out, s_expiryDate);
        var result = new SmileAwareBarrierPricer().Price(option, market, calibration);

        result.IsSuccess.Should().BeTrue();
        result.Price.Amount.Should().Be(0m, "knock-out barrier breached at spot → option has zero value");
        result.Diagnostics.Should().ContainKey("barrierBreachedAtSpot");
    }

    [Fact]
    public void SmileAwarePrice_UpInKnockIn_AlreadyBreachedAtSpot_ReturnsVanillaPrice()
    {
        // spot=100, Up-In call, barrier=90 → spot >= barrier → already knocked in → returns vanilla price.
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20);
        int steps = 50;

        var calibration = new ImpliedBinomialTreeCalibrator().Calibrate(
            new ImpliedVolatilityTreeCalibrationInput(surface, steps, market));
        calibration.IsSuccess.Should().BeTrue();

        var option = new BarrierOption(CallPut.Call, 100m, Currency.USD, 90m, BarrierDirection.Up, BarrierKnockType.In, s_expiryDate);
        var result = new SmileAwareBarrierPricer().Price(option, market, calibration);

        // The breached knock-in should price as vanilla (greater than zero)
        result.IsSuccess.Should().BeTrue();
        result.Price.Amount.Should().BeGreaterThan(0m, "knocked-in option with spot above strike is valuable");
        result.Diagnostics.Should().ContainKey("barrierBreachedAtSpot");
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
