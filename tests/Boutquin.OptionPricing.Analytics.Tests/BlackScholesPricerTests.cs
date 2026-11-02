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
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Analytics.Parity;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Analytics.Tests;

public sealed class BlackScholesPricerTests
{
    [Fact]
    public void PutCallParity_ShouldHoldApproximately()
    {
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var pricer = new BlackScholesPricer();
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
        var put = new EuropeanOption(CallPut.Put, 100m, Currency.USD, call.ExpiryDate);
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);

        var callPrice = pricer.Price(call, market).Price;
        var putPrice = pricer.Price(put, market).Price;
        var impliedCall = PutCallParity.ImpliedCallPrice(putPrice.Amount, market.Spot.Amount, call.Strike.Amount, market, call.ExpiryDate);

        impliedCall.Should().BeApproximately(callPrice.Amount, 0.01m);
    }

    [Theory]
    // External oracle: Black-Scholes prices computed independently (erf-based reference,
    // cross-checked against Hull "Options, Futures & Other Derivatives" §17). The market is
    // engineered so the curve's ACT/365 year fraction is exact (integer day spans) and the
    // continuously-compounded rate is recovered verbatim, so these absolute prices pin the
    // formula — a self-consistency identity (e.g. put-call parity) would pass even with a
    // systematic sign/compounding error; an external value will not.
    [InlineData(CallPut.Call, 100.0, 100.0, 365, 0.05, 0.20, 0.00, 10.450584)]
    [InlineData(CallPut.Put, 100.0, 100.0, 365, 0.05, 0.20, 0.00, 5.573526)]
    [InlineData(CallPut.Call, 100.0, 100.0, 365, 0.05, 0.20, 0.03, 8.652529)]  // dividend lowers the call
    [InlineData(CallPut.Put, 100.0, 100.0, 365, 0.05, 0.20, 0.03, 6.730918)]   // dividend raises the put
    [InlineData(CallPut.Call, 100.0, 90.0, 365, 0.05, 0.25, 0.00, 18.140763)]  // in-the-money call
    [InlineData(CallPut.Put, 100.0, 110.0, 365, 0.05, 0.25, 0.00, 12.661621)]  // in-the-money put
    [InlineData(CallPut.Call, 100.0, 100.0, 730, 0.05, 0.20, 0.00, 16.126780)] // two-year tenor
    public void Price_MatchesExternalReferenceValues(
        CallPut callPut, double spot, double strike, int daysToExpiry,
        double riskFreeRate, double volatility, double dividendYield, double expected)
    {
        var valuationDate = new DateOnly(2026, 1, 15);
        var market = CreateMarket(valuationDate, (decimal)spot, volatility, riskFreeRate, dividendYield);
        var option = new EuropeanOption(callPut, (decimal)strike, Currency.USD, valuationDate.AddDays(daysToExpiry));

        var price = new BlackScholesPricer().Price(option, market).Price;

        // 1e-3 is far tighter than any sign/compounding error (which shift the price by
        // tenths to whole points) yet comfortably absorbs double→decimal rounding and the
        // high-precision-CDF vs reference-erf gap (~1e-7 here).
        ((double)price.Amount).Should().BeApproximately(expected, 1e-3);
    }

    private static OptionMarketData CreateMarket(DateOnly valuationDate, decimal spot, double flatVolatility, double riskFreeRate, double dividendYield)
    {
        return new OptionMarketData(
            valuationDate,
            Currency.USD.Amount(spot),
            flatVolatility,
            new FlatDiscountCurve(new CurveName("USD-OIS"), valuationDate, Currency.USD, riskFreeRate),
            new FlatDiscountCurve(new CurveName("DIVIDEND"), valuationDate, Currency.USD, dividendYield));
    }
}
