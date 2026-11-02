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
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Exotics.Barriers;
using Boutquin.OptionPricing.Trees.Binomial;
using FluentAssertions;

namespace Boutquin.OptionPricing.Exotics.Tests;

public sealed class RcBarrierRegressionTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MonitoringAdjustment_RejectsNonFiniteVolAndTime(double invalid)
    {
        Action vol = () => BarrierMonitoringAdjustments.AdjustedBarrierLevel(120m, BarrierDirection.Up, invalid, 1d, 12);
        Action time = () => BarrierMonitoringAdjustments.AdjustedBarrierLevel(120m, BarrierDirection.Up, 0.2, invalid, 12);
        vol.Should().Throw<ArgumentOutOfRangeException>();
        time.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MonitoringAdjustment_RejectsUnrepresentableResultAsInputError()
    {
        Action act = () => BarrierMonitoringAdjustments.AdjustedBarrierLevel(120m, BarrierDirection.Up, 1000d, 1d, 1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
    [Theory]
    [InlineData(Currency.USD)]
    [InlineData(Currency.CAD)]
    [InlineData(Currency.EUR)]
    public void BothPricers_MatchIndependentExhaustivePathOracleAcrossBarrierMatrix(Currency currency)
    {
        var date = new DateOnly(2026, 1, 1);
        var market = new OptionMarketData(date, currency.Amount(100m), 0.2,
            new FlatDiscountCurve(new CurveName("r"), date, currency, 0.05));
        const int steps = 4;
        var spots = Enumerable.Range(0, steps + 1).Select(k => (IReadOnlyList<double>)Enumerable.Range(0, k + 1)
            .Select(j => 100d * Math.Pow(1.2, k - j) * Math.Pow(0.85, j)).ToArray()).ToArray();
        var probabilities = Enumerable.Range(0, steps).Select(k => (IReadOnlyList<double>)Enumerable.Range(0, k + 1)
            .Select(j => 0.2 + (0.1 * (j % 3))).ToArray()).ToArray();
        var tree = new BinomialTree(steps, 0.25, 1.2, 0.85, 0.4, spots) { ProbabilityLattice = probabilities, RiskFreeRate = 0.05 };
        var calibration = new ImpliedTreeResult(tree, Array.Empty<IReadOnlyList<double>>(), Array.Empty<IReadOnlyList<double>>(),
            new CalibrationReport("path-grid", CalculationStatus.Success, new Dictionary<string, object?>()));
        foreach (CallPut callPut in Enum.GetValues<CallPut>())
        {
            foreach (BarrierDirection direction in Enum.GetValues<BarrierDirection>())
            {
                foreach (BarrierKnockType knock in Enum.GetValues<BarrierKnockType>())
                {
                    var option = new BarrierOption(callPut, 100m, currency, direction == BarrierDirection.Up ? 130m : 80m,
                        direction, knock, date.AddYears(1));
                    double expected = 0d;
                    for (int path = 0; path < 1 << steps; path++)
                    {
                        double weight = 1d;
                        int node = 0;
                        bool touched = false;
                        for (int step = 0; step <= steps; step++)
                        {
                            double spot = spots[step][node];
                            touched |= direction == BarrierDirection.Up ? spot >= (double)option.BarrierLevel : spot <= (double)option.BarrierLevel;
                            if (step == steps)
                            {
                                bool active = knock == BarrierKnockType.In ? touched : !touched;
                                double payoff = Math.Max(callPut == CallPut.Call ? spot - 100d : 100d - spot, 0d);
                                if (active)
                                {
                                    expected += weight * payoff;
                                }
                            }
                            else
                            {
                                bool up = (path & (1 << step)) != 0;
                                double p = probabilities[step][node];
                                weight *= up ? p : 1d - p;
                                if (!up)
                                {
                                    node++;
                                }
                            }
                        }
                    }
                    expected *= Math.Exp(-0.05);
                    var flat = new BarrierTreePricer().Price(option, market, tree);
                    var smile = new SmileAwareBarrierPricer().Price(option, market, calibration);
                    ((double)flat.Price.Amount).Should().BeApproximately(expected, 1e-10);
                    ((double)smile.Price.Amount).Should().BeApproximately(expected, 1e-10);
                    flat.Price.Currency.Should().Be(currency);
                    smile.Price.Currency.Should().Be(currency);
                }
            }
        }
    }

    [Fact]
    public void DownOutAtOrBelowSpot_IsExtinguishedForBothPricers_AndGuardsArePinned()
    {
        var date = new DateOnly(2026, 1, 1);
        var market = new OptionMarketData(date, Currency.CAD.Amount(100m), 0.2,
            new FlatDiscountCurve(new CurveName("r"), date, Currency.CAD, 0.05));
        var tree = new CoxRossRubinsteinTreeBuilder().Build(TreeModelInput.FromMarketData(market, 100m, date.AddYears(1), 8));
        var calibration = new ImpliedTreeResult(tree, Array.Empty<IReadOnlyList<double>>(), Array.Empty<IReadOnlyList<double>>(),
            new CalibrationReport("tree", CalculationStatus.Success, new Dictionary<string, object?>()));
        var smile = new SmileAwareBarrierPricer();
        foreach (decimal barrier in new[] { 100m, 110m })
        {
            var option = new BarrierOption(CallPut.Put, 100m, Currency.CAD, barrier, BarrierDirection.Down, BarrierKnockType.Out, date.AddYears(1));
            new BarrierTreePricer().Price(option, market, tree).Price.Amount.Should().Be(0m);
            smile.Price(option, market, calibration).Price.Amount.Should().Be(0m);
            Action nullOption = () => smile.Price(null!, market, calibration);
            nullOption.Should().Throw<ArgumentNullException>();
            Action nullMarket = () => smile.Price(option, null!, calibration);
            nullMarket.Should().Throw<ArgumentNullException>();
            Action nullCalibration = () => smile.Price(option, market, null!);
            nullCalibration.Should().Throw<ArgumentNullException>();
            Action currency = () => smile.Price(new BarrierOption(CallPut.Put, 100m, Currency.USD, 110m,
                BarrierDirection.Down, BarrierKnockType.Out, date.AddYears(1)), market, calibration);
            currency.Should().Throw<Boutquin.Domain.Exceptions.CurrencyMismatchException>();
        }
        var downOut = new BarrierOption(CallPut.Put, 100m, Currency.CAD, 80m, BarrierDirection.Down, BarrierKnockType.Out, date.AddYears(1));
        var pricer = new BarrierTreePricer();
        // Lowering a Down barrier makes knock-out LESS likely, so discrete KO is no cheaper.
        pricer.PriceDiscretelyMonitored(downOut, market, tree, 12).Price.Amount.Should()
            .BeGreaterThanOrEqualTo(pricer.Price(downOut, market, tree).Price.Amount);
    }
    [Fact]
    public void OutOfDecimalRangePrice_FailsWithNumericalContext()
    {
        var date = new DateOnly(2026, 1, 1);
        var market = new OptionMarketData(date, Currency.CAD.Amount(100m), 0.2,
            new FlatDiscountCurve(new CurveName("r"), date, Currency.CAD, 0d));
        var tree = new BinomialTree(1, 1d, 1e98, 0.01, 0.5,
            new IReadOnlyList<double>[] { new[] { 100d }, new[] { 1e100, 1d } })
        { RiskFreeRate = 0d };
        var option = new BarrierOption(CallPut.Call, 100m, Currency.CAD, 0.1m, BarrierDirection.Down, BarrierKnockType.Out, date.AddYears(1));
        Action act = () => new BarrierTreePricer().Price(option, market, tree);
        act.Should().Throw<InvalidOperationException>().WithMessage("*decimal range*");
    }
}
