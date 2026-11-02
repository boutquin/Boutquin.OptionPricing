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
using Boutquin.OptionPricing.Calibration.ImpliedVolatility;
using Boutquin.OptionPricing.Calibration.Smile;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Calibration.Tests;

public sealed class RcCalibrationRegressionTests
{
    private static readonly DateOnly s_date = new(2026, 1, 1);
    private static OptionMarketData Market(double rate = 0.05) => new(s_date, Currency.USD.Amount(100m), 0.2,
        new FlatDiscountCurve(new CurveName("USD"), s_date, Currency.USD, rate));
    private static VolatilitySurface Surface() => new(new[] { new SmileSlice(1d, new[] { new SurfacePoint(1d, 100m, 0.2) }) });

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Validator_RejectsNonFiniteVol(double vol)
    {
        new QuoteArbitrageValidator().Validate(new SmileSlice(1d, new[] { new SurfacePoint(1d, 100m, vol) }))
            .IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(100, 90, 110)]
    [InlineData(90, 90, 110)]
    public void Validator_RejectsNonIncreasingStrikes(int a, int b, int c)
    {
        var slice = new SmileSlice(1d, new[] { new SurfacePoint(1d, a, 0.2), new SurfacePoint(1d, b, 0.2), new SurfacePoint(1d, c, 0.2) });
        new QuoteArbitrageValidator().Validate(slice).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Surface_CopiesBothAxes_AndCannotBeMutatedByCasting()
    {
        var points = new List<SurfacePoint> { new(1d, 100m, 0.2) };
        var slices = new List<SmileSlice> { new(1d, points) };
        var surface = new VolatilitySurface(slices);
        points[0] = new SurfacePoint(1d, 100m, 0.8);
        slices.Clear();
        surface.GetVol(100m, 1d).Should().Be(0.2);
        Action mutate = () => ((IList<SurfacePoint>)surface.Slices[0].Points)[0] = new SurfacePoint(1d, 100m, 0.9);
        mutate.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Surface_RejectsInvalidMaturity(double maturity)
    {
        Action act = () => _ = new VolatilitySurface(new[] { new SmileSlice(maturity, new[] { new SurfacePoint(maturity, 100m, 0.2) }) });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Surface_QueryRejectsNaN()
    {
        Action act = () => Surface().GetVol(100m, double.NaN);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Provider_RejectsNullAndNonPositiveStrike()
    {
        Action nullSurface = () => _ = new SurfaceDerivedPriceProvider(null!, Market(), s_date.AddYears(1));
        nullSurface.Should().Throw<ArgumentNullException>();
        var provider = new SurfaceDerivedPriceProvider(Surface(), Market(), s_date.AddYears(1));
        Action strike = () => provider.GetCallPrice(0m, 1d);
        strike.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Calibrators_CarryBuildRate(bool volatilityTree)
    {
        var market = Market();
        var input = new ImpliedVolatilityTreeCalibrationInput(Surface(), 8, market);
        var result = volatilityTree ? new ImpliedVolatilityTreeCalibrator().Calibrate(input) : new ImpliedBinomialTreeCalibrator().Calibrate(input);
        result.Tree.RiskFreeRate.Should().BeApproximately(0.05, 1e-12);
        // A remote up-out barrier leaves the vanilla payoff unchanged. Reusing the
        // calibration against an alternate price-time discount curve must retain AD pricing.
        double expected = result.ArrowDebreuPrices[^1].Select((p, j) => p * Math.Max(result.Tree.TerminalSpots[j] - 100d, 0d)).Sum();
        var option = new BarrierOption(CallPut.Call, 100m, Currency.USD, 100000m, BarrierDirection.Up, BarrierKnockType.Out, s_date.AddYears(1));
        // Exotics is deliberately not referenced by Calibration.Tests; price via tree pricer.
        var vanilla = new EuropeanOption(CallPut.Call, 100m, Currency.USD, option.ExpiryDate);
        double actual = (double)new Boutquin.OptionPricing.Trees.Pricing.EuropeanTreePricer().Price(vanilla, Market(0.3), result.Tree).Price.Amount;
        actual.Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void Calibrator_RejectsNonFiniteQuoteInsteadOfClaimingSuccess()
    {
        var input = new ImpliedVolatilityTreeCalibrationInput(Surface(), 2, Market(), new InvalidProvider());
        Action act = () => new ImpliedBinomialTreeCalibrator().Calibrate(input);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResultDiagnostics_AreSnapshotsOnConstructorAndWithPaths()
    {
        var values = new Dictionary<string, object?> { ["x"] = 1 };
        var report = new CalibrationReport("model", CalculationStatus.Success, values);
        var iv = new ImpliedVolatilityResult(0.2, CalculationStatus.Success, 1, values);
        var copy = report with { Diagnostics = values };
        values["x"] = 2;
        report.Diagnostics["x"].Should().Be(1);
        copy.Diagnostics["x"].Should().Be(1);
        iv.Diagnostics["x"].Should().Be(1);
    }

    [Fact]
    public void Solver_RejectsMissingMarketAndDoesNotCalibrateExpiredOption()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_date);
        var solver = new ImpliedVolatilitySolver();
        Action act = () => solver.Solve(new ImpliedVolatilityRequest(option, null!, 0m, 0.2));
        act.Should().Throw<ArgumentNullException>();
        solver.Solve(new ImpliedVolatilityRequest(option, Market(), 0m, 0.2)).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void SkewedTree_OrderedNodesMartingaleAndStatePricesHoldAtEveryLevel()
    {
        var points = new[] { 70m, 80m, 90m, 100m, 110m, 120m, 130m }
            .Select(k => new SurfacePoint(0.5, k, 0.2 - (0.002 * ((double)k - 100d)))).ToArray();
        var market = Market();
        var surface = new VolatilitySurface(new[] { new SmileSlice(0.5, points) });
        var result = new ImpliedBinomialTreeCalibrator().Calibrate(new(surface, 20, market));
        double dt = result.Tree.DeltaTime;
        for (int step = 0; step < result.Tree.Steps; step++)
        {
            result.Tree.SpotLattice[step + 1].Should().BeInDescendingOrder();
            result.ArrowDebreuPrices[step + 1].Should().OnlyContain(x => double.IsFinite(x) && x >= 0d);
            result.ArrowDebreuPrices[step + 1].Sum().Should().BeApproximately(Math.Exp(-0.05 * (step + 1) * dt), 1e-12);
            for (int j = 0; j <= step; j++)
            {
                double up = result.Tree.SpotLattice[step + 1][j];
                double down = result.Tree.SpotLattice[step + 1][j + 1];
                up.Should().BeGreaterThan(down);
                double probability = result.Tree.ProbabilityLattice![step][j];
                double actual = (probability * up) + ((1d - probability) * down);
                actual.Should().BeApproximately(result.Tree.SpotLattice[step][j] * Math.Exp(0.05 * dt), 1e-8);
            }
        }
    }

    [Fact]
    public void InvalidQuotesCannotMasqueradeAsSuccessfulCalibrationButFiniteReversionsWarn()
    {
        var result = new ImpliedBinomialTreeCalibrator().Calibrate(new(Surface(), 10, Market(), new HighPriceProvider()));
        result.Report.Status.Should().Be(CalculationStatus.ConvergedWithWarnings);
        ((int)result.Report.Diagnostics["nodeReversions"]!).Should().BeGreaterThan(16);
    }

    [Fact]
    public void CalendarCheck_OnlyComparesCoLocatedStrikesAsDocumented()
    {
        var surface = new VolatilitySurface(new[]
        {
            new SmileSlice(0.25, new[] { new SurfacePoint(0.25, 100m, 0.5) }),
            new SmileSlice(1d, new[] { new SurfacePoint(1d, 101m, 0.1) })
        });
        new QuoteArbitrageValidator().Validate(surface).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ResultLattices_CopiesInnerRowsOnConstructorAndWithPaths()
    {
        var original = new ImpliedBinomialTreeCalibrator().Calibrate(new(Surface(), 2, Market()));
        var row = new[] { 1d };
        IReadOnlyList<IReadOnlyList<double>> rows = new[] { row };
        var result = original with { LocalVolatilities = rows, ArrowDebreuPrices = rows };
        row[0] = 2d;
        result.LocalVolatilities[0][0].Should().Be(1d);
        result.ArrowDebreuPrices[0][0].Should().Be(1d);
        Action mutate = () => ((IList<double>)result.LocalVolatilities[0])[0] = 3d;
        mutate.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Solver_NegativePremiumIsRejectedBeforeCalibration()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_date.AddYears(1));
        Action act = () => new ImpliedVolatilitySolver().Solve(new(option, Market(), -1m, 0.2));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Validator_RejectsNonrepresentableTotalVarianceRatherThanPassingInfinityComparisons()
    {
        var surface = new VolatilitySurface(new[]
        {
            new SmileSlice(0.5, new[] { new SurfacePoint(0.5, 100m, 2e200) }),
            new SmileSlice(1d, new[] { new SurfacePoint(1d, 100m, 1e200) })
        });
        new QuoteArbitrageValidator().Validate(surface).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Builder_UsesExactMaturityCoordinatesRatherThanMergingDistinctSlices()
    {
        var surface = new VolatilitySurfaceBuilder().Build(new[]
        {
            new SurfacePoint(1d, 100m, 0.2),
            new SurfacePoint(1d + 1e-10, 100m, 0.3)
        });
        surface.Slices.Should().HaveCount(2);
        surface.GetVol(100m, 1d).Should().Be(0.2);
        surface.GetVol(100m, 1d + 1e-10).Should().Be(0.3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Provider_RejectsFiniteButUnrepresentableVariance(bool put)
    {
        var surface = new VolatilitySurface(new[] { new SmileSlice(1d, new[] { new SurfacePoint(1d, 100m, 1e200) }) });
        var provider = new SurfaceDerivedPriceProvider(surface, Market(), s_date.AddYears(1));
        Action act = () => _ = put ? provider.GetPutPrice(100m, 1d) : provider.GetCallPrice(100m, 1d);
        act.Should().Throw<InvalidOperationException>().WithMessage("*variance*");
    }

    private sealed class HighPriceProvider : IOptionPriceProvider
    {
        public double GetCallPrice(decimal strike, double maturityInYears) => 10000d;
        public double GetPutPrice(decimal strike, double maturityInYears) => 10000d;
    }

    private sealed class InvalidProvider : IOptionPriceProvider
    {
        public double GetCallPrice(decimal strike, double maturityInYears) => double.NaN;
        public double GetPutPrice(decimal strike, double maturityInYears) => double.NaN;
    }
}
