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
using FsCheck.Xunit;

namespace Boutquin.OptionPricing.PropertyTests;

public sealed class PutCallParityProperties
{
    [Property(MaxTest = 20)]
    public void Parity_ShouldHold_ForReasonableInputs(decimal rawSpot, decimal rawStrike)
    {
        decimal spot = Math.Abs(rawSpot % 200m) + 20m;
        decimal strike = Math.Abs(rawStrike % 200m) + 20m;
        DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);

        var market = CreateMarket(valuationDate, spot, 0.20d, 0.03d, 0.01d);
        var call = new EuropeanOption(CallPut.Call, strike, valuationDate.AddMonths(6));
        var put = new EuropeanOption(CallPut.Put, strike, call.ExpiryDate);
        var pricer = new BlackScholesPricer();

        decimal callPrice = pricer.Price(call, market).Price;
        decimal putPrice = pricer.Price(put, market).Price;
        decimal impliedCall = PutCallParity.ImpliedCallPrice(putPrice, spot, strike, market, call.ExpiryDate);

        impliedCall.Should().BeApproximately(callPrice, 0.02m);
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
