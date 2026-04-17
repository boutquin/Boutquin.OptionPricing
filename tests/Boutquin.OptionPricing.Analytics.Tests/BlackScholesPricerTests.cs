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
        DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
        var pricer = new BlackScholesPricer();
        var call = new EuropeanOption(CallPut.Call, 100m, valuationDate.AddMonths(6));
        var put = new EuropeanOption(CallPut.Put, 100m, call.ExpiryDate);
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);

        var callPrice = pricer.Price(call, market).Price;
        var putPrice = pricer.Price(put, market).Price;
        var impliedCall = PutCallParity.ImpliedCallPrice(putPrice, market.Spot, call.Strike, market, call.ExpiryDate);

        impliedCall.Should().BeApproximately(callPrice, 0.01m);
    }

    private static OptionMarketData CreateMarket(DateOnly valuationDate, decimal spot, double flatVolatility, double riskFreeRate, double dividendYield)
    {
        return new OptionMarketData(
            valuationDate,
            spot,
            flatVolatility,
            new FlatDiscountCurve(new CurveName("USD-OIS"), valuationDate, CurrencyCode.USD, riskFreeRate),
            new FlatDiscountCurve(new CurveName("DIVIDEND"), valuationDate, CurrencyCode.USD, dividendYield));
    }
}
