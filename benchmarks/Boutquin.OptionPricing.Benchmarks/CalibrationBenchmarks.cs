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
using Boutquin.OptionPricing.Calibration.ImpliedVolatility;
using Boutquin.OptionPricing.Calibration.Smile;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;

namespace Boutquin.OptionPricing.Benchmarks;

/// <summary>
/// Benchmarks for implied volatility solving, smile construction, and implied tree calibration.
/// </summary>
[MemoryDiagnoser]
public class CalibrationBenchmarks
{
    private readonly DateOnly _valuationDate = DateOnly.FromDateTime(DateTime.Today);
    private ImpliedVolatilitySolver _ivSolver = null!;
    private ImpliedVolatilityRequest _ivRequest = null!;
    private VolatilitySurfaceBuilder _surfaceBuilder = null!;
    private SurfacePoint[] _surfacePoints = null!;
    private ImpliedVolatilityTreeCalibrator _dkCalibrator = null!;
    private ImpliedVolatilityTreeCalibrationInput _calibInput = null!;

    /// <summary>
    /// Initializes benchmark inputs.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var option = new EuropeanOption(CallPut.Call, 100m, _valuationDate.AddMonths(6));
        var market = new OptionMarketData(
            _valuationDate, 100m, 0.20d,
            new FlatDiscountCurve(new CurveName("USD-OIS"), _valuationDate, CurrencyCode.USD, 0.03d),
            new FlatDiscountCurve(new CurveName("DIVIDEND"), _valuationDate, CurrencyCode.USD, 0.01d));

        // IV solver setup
        _ivSolver = new ImpliedVolatilitySolver();
        decimal observedPrice = new Analytics.BlackScholes.BlackScholesPricer().Price(option, market).Price;
        _ivRequest = new ImpliedVolatilityRequest(
            option, market.WithFlatVolatility(0.10d), observedPrice, 0.15d);

        // Smile construction setup
        _surfaceBuilder = new VolatilitySurfaceBuilder();
        _surfacePoints =
        [
            new(0.25d, 80m, 0.30d), new(0.25d, 90m, 0.25d), new(0.25d, 100m, 0.20d),
            new(0.25d, 110m, 0.22d), new(0.25d, 120m, 0.27d),
            new(0.50d, 80m, 0.28d), new(0.50d, 90m, 0.24d), new(0.50d, 100m, 0.20d),
            new(0.50d, 110m, 0.22d), new(0.50d, 120m, 0.26d)
        ];

        // Implied tree calibration setup
        var surface = _surfaceBuilder.Build(_surfacePoints);
        _dkCalibrator = new ImpliedVolatilityTreeCalibrator();
        _calibInput = new ImpliedVolatilityTreeCalibrationInput(surface, 30, market);
    }

    /// <summary>Solves for implied volatility via bisection.</summary>
    [Benchmark]
    public double ImpliedVolSolve()
    {
        return _ivSolver.Solve(_ivRequest).ImpliedVolatility!.Value;
    }

    /// <summary>Builds a volatility surface from raw quotes.</summary>
    [Benchmark]
    public VolatilitySurface SmileConstruction()
    {
        return _surfaceBuilder.Build(_surfacePoints);
    }

    /// <summary>Calibrates a Derman-Kani implied volatility tree.</summary>
    [Benchmark]
    public ImpliedTreeResult ImpliedTreeCalibration()
    {
        return _dkCalibrator.Calibrate(_calibInput);
    }
}
