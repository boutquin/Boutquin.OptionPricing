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

using BenchmarkDotNet.Attributes;
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

namespace Boutquin.OptionPricing.Benchmarks;

/// <summary>
/// Benchmarks for barrier option pricing (flat-vol and smile-aware).
/// </summary>
[MemoryDiagnoser]
public class ExoticsBenchmarks
{
    private readonly DateOnly _valuationDate = DateOnly.FromDateTime(DateTime.Today);
    private BarrierTreePricer _flatPricer = null!;
    private SmileAwareBarrierPricer _smilePricer = null!;
    private BarrierOption _barrierOption = null!;
    private OptionMarketData _market = null!;
    private BinomialTree _flatTree = null!;
    private ImpliedTreeResult _calibResult = null!;

    /// <summary>
    /// Initializes benchmark inputs.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        DateOnly expiry = _valuationDate.AddMonths(6);
        _market = new OptionMarketData(
            _valuationDate, 100m, 0.20d,
            new FlatDiscountCurve(new CurveName("USD-OIS"), _valuationDate, CurrencyCode.USD, 0.03d),
            new FlatDiscountCurve(new CurveName("DIVIDEND"), _valuationDate, CurrencyCode.USD, 0.01d));

        _barrierOption = new BarrierOption(CallPut.Call, 100m, 80m, BarrierDirection.Down, BarrierKnockType.Out, expiry);

        // Flat-vol tree
        var input = new TreeModelInput(_market.Spot, 100m, _valuationDate, expiry,
            _market.DiscountCurve, _market.FlatVolatility, 200, _market.DividendCurve, _market.BorrowCurve);
        _flatTree = new CoxRossRubinsteinTreeBuilder().Build(input);
        _flatPricer = new BarrierTreePricer();

        // Smile-aware calibration
        SurfacePoint[] quotes =
        [
            new(0.5d, 80m, 0.28d), new(0.5d, 90m, 0.24d), new(0.5d, 100m, 0.20d),
            new(0.5d, 110m, 0.22d), new(0.5d, 120m, 0.26d)
        ];
        var surface = new VolatilitySurfaceBuilder().Build(quotes);
        var calibInput = new ImpliedVolatilityTreeCalibrationInput(surface, 50, _market);
        _calibResult = new ImpliedVolatilityTreeCalibrator().Calibrate(calibInput);
        _smilePricer = new SmileAwareBarrierPricer();
    }

    /// <summary>Prices a down-and-out call on a flat-vol CRR tree.</summary>
    [Benchmark]
    public decimal BarrierFlatVol()
    {
        return _flatPricer.Price(_barrierOption, _market, _flatTree).Price;
    }

    /// <summary>Prices a down-and-out call on a smile-aware implied tree.</summary>
    [Benchmark]
    public decimal BarrierSmileAware()
    {
        return _smilePricer.Price(_barrierOption, _market, _calibResult).Price;
    }
}
