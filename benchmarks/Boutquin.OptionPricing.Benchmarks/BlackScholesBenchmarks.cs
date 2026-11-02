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
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;

namespace Boutquin.OptionPricing.Benchmarks;

/// <summary>
/// BenchmarkDotNet harness for representative Black-Scholes pricing throughput.
/// </summary>
[MemoryDiagnoser]
public class BlackScholesBenchmarks
{
    private readonly BlackScholesPricer _pricer = new();
    private readonly DateOnly _valuationDate = DateOnly.FromDateTime(DateTime.Today);
    private readonly EuropeanOption _option;
    private readonly OptionMarketData _market;

    /// <summary>
    /// Initializes benchmark inputs used by each measured invocation.
    /// </summary>
    public BlackScholesBenchmarks()
    {
        _option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, _valuationDate.AddMonths(6));
        _market = new OptionMarketData(
            _valuationDate,
            Currency.USD.Amount(100m),
            0.20d,
            new FlatDiscountCurve(new CurveName("USD-OIS"), _valuationDate, Currency.USD, 0.03d),
            new FlatDiscountCurve(new CurveName("DIVIDEND"), _valuationDate, Currency.USD, 0.01d));
    }

    /// <summary>
    /// Prices a vanilla European call under Black-Scholes assumptions.
    /// </summary>
    /// <returns>The model price for the configured benchmark contract.</returns>
    [Benchmark]
    public decimal PriceEuropeanCall()
    {
        return _pricer.Price(_option, _market).Price.Amount;
    }
}
