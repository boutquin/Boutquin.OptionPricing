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
using FsCheck.Xunit;

namespace Boutquin.OptionPricing.PropertyTests;

public sealed class PutCallParityProperties
{
    [Property(MaxTest = 200)]
    public void Parity_ShouldHold_ForReasonableInputs(decimal rawSpot, decimal rawStrike, byte rawVol, byte rawRate, byte rawDiv, byte rawBorrow)
    {
        decimal spot = Math.Abs(rawSpot % 200m) + 20m;
        decimal strike = Math.Abs(rawStrike % 200m) + 20m;
        // Randomize volatility, rate, and dividend yield across reasonable ranges
        // (put-call parity is independent of all three, so it must hold throughout).
        double vol = 0.05d + ((rawVol % 60) / 100d);   // 0.05 .. 0.64
        double rate = ((rawRate % 10) - 5) / 100d;    // -0.05 .. 0.04
        double dividend = (rawDiv % 5) / 100d;         // 0.00 .. 0.04
        double borrow = (rawBorrow % 5) / 100d;        // 0.00 .. 0.04
        DateOnly valuationDate = new(2026, 6, 15);

        var market = CreateMarket(valuationDate, spot, vol, rate, dividend, borrow);
        var call = new EuropeanOption(CallPut.Call, strike, Currency.USD, valuationDate.AddMonths(6));
        var put = new EuropeanOption(CallPut.Put, strike, Currency.USD, call.ExpiryDate);
        var pricer = new BlackScholesPricer();

        decimal callPrice = pricer.Price(call, market).Price.Amount;
        decimal putPrice = pricer.Price(put, market).Price.Amount;
        decimal impliedCall = PutCallParity.ImpliedCallPrice(putPrice, spot, strike, market, call.ExpiryDate);

        decimal impliedPut = PutCallParity.ImpliedPutPrice(callPrice, spot, strike, market, call.ExpiryDate);
        impliedCall.Should().BeApproximately(callPrice, 1e-8m);
        impliedPut.Should().BeApproximately(putPrice, 1e-8m);
    }

    private static OptionMarketData CreateMarket(DateOnly valuationDate, decimal spot, double flatVolatility, double riskFreeRate, double dividendYield, double borrowRate)
    {
        return new OptionMarketData(
            valuationDate,
            Currency.USD.Amount(spot),
            flatVolatility,
            new FlatDiscountCurve(new CurveName("USD-OIS"), valuationDate, Currency.USD, riskFreeRate),
            new FlatDiscountCurve(new CurveName("DIVIDEND"), valuationDate, Currency.USD, dividendYield),
            new FlatDiscountCurve(new CurveName("BORROW"), valuationDate, Currency.USD, borrowRate));
    }
}
