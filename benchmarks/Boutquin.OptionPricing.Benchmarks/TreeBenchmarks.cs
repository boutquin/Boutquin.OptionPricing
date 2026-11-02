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
using Boutquin.Curves.Core.Discounting;
using Boutquin.Domain.Enumerations;
using Boutquin.Domain.Extensions;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Trees.Binomial;
using Boutquin.OptionPricing.Trees.Pricing;

namespace Boutquin.OptionPricing.Benchmarks;

/// <summary>
/// BenchmarkDotNet harness for CRR tree construction and European/American pricing
/// at varying step counts.
/// </summary>
[MemoryDiagnoser]
public class TreeBenchmarks
{
    private readonly DateOnly _valuationDate = DateOnly.FromDateTime(DateTime.Today);
    private readonly CoxRossRubinsteinTreeBuilder _builder = new();
    private readonly EuropeanTreePricer _euroPricer = new();
    private readonly AmericanTreePricer _americanPricer = new();
    private OptionMarketData _market = null!;
    private EuropeanOption _euroOption = null!;
    private AmericanOption _americanOption = null!;

    /// <summary>Number of tree steps.</summary>
    [Params(50, 200, 1000)]
    public int Steps { get; set; }

    /// <summary>
    /// Initializes benchmark inputs.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _euroOption = new EuropeanOption(CallPut.Call, 100m, Currency.USD, _valuationDate.AddMonths(6));
        _americanOption = new AmericanOption(CallPut.Put, 100m, Currency.USD, _valuationDate.AddMonths(6));
        _market = new OptionMarketData(
            _valuationDate, Currency.USD.Amount(100m), 0.20d,
            new FlatDiscountCurve(new CurveName("USD-OIS"), _valuationDate, Currency.USD, 0.03d),
            new FlatDiscountCurve(new CurveName("DIVIDEND"), _valuationDate, Currency.USD, 0.01d));
    }

    /// <summary>Builds a CRR tree and prices a European call.</summary>
    [Benchmark]
    public decimal EuropeanCallTreePrice()
    {
        var input = new TreeModelInput(_market.Spot.Amount, _euroOption.Strike.Amount, _market.ValuationDate,
            _euroOption.ExpiryDate, _market.DiscountCurve, _market.FlatVolatility, Steps,
            _market.DividendCurve, _market.BorrowCurve);
        var tree = _builder.Build(input);
        return _euroPricer.Price(_euroOption, _market, tree).Price.Amount;
    }

    /// <summary>Builds a CRR tree and prices an American put.</summary>
    [Benchmark]
    public decimal AmericanPutTreePrice()
    {
        var input = new TreeModelInput(_market.Spot.Amount, _americanOption.Strike.Amount, _market.ValuationDate,
            _americanOption.ExpiryDate, _market.DiscountCurve, _market.FlatVolatility, Steps,
            _market.DividendCurve, _market.BorrowCurve);
        var tree = _builder.Build(input);
        return _americanPricer.Price(_americanOption, _market, tree).Price.Amount;
    }
}
