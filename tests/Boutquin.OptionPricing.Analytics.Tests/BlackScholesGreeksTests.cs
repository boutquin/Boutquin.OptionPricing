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
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Domain.Results;
using FluentAssertions;

namespace Boutquin.OptionPricing.Analytics.Tests;

/// <summary>
/// Numerical and contract tests for <see cref="BlackScholesGreeks"/>: per-Greek sign
/// sanity for calls and puts (F12 coverage gap), the per-calendar-day theta convention
/// validated against a one-day reprice (F27), invalid-enum symmetry with the pricer (F12),
/// and the expired-option collapse.
/// </summary>
public sealed class BlackScholesGreeksTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 15);
    private const Currency Usd = Currency.USD;

    private static OptionMarketData CreateMarket(DateOnly valuationDate, double r = 0.05, double q = 0.0)
    {
        return new OptionMarketData(
            valuationDate,
            Currency.USD.Amount(100m),
            flatVolatility: 0.20,
            discountCurve: new FlatDiscountCurve(new CurveName("USD-OIS"), valuationDate, Usd, r),
            dividendCurve: new FlatDiscountCurve(new CurveName("DIV"), valuationDate, Usd, q));
    }

    [Fact]
    public void CalculateGreeks_Call_HasCanonicalSigns()
    {
        var greeks = new BlackScholesGreeks();
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));

        GreekSet g = greeks.CalculateGreeks(call, CreateMarket(s_valuationDate));

        g.Delta.Should().BeInRange(0d, 1d);
        g.Gamma.Should().BeGreaterThan(0d);
        g.Vega.Should().BeGreaterThan(0d);
        g.Theta.Should().BeLessThan(0d);   // long call bleeds time value
        g.Rho.Should().BeGreaterThan(0d);  // call rho positive
    }

    [Fact]
    public void CalculateGreeks_Put_HasCanonicalSigns()
    {
        var greeks = new BlackScholesGreeks();
        var put = new EuropeanOption(CallPut.Put, 100m, Currency.USD, s_valuationDate.AddMonths(6));

        GreekSet g = greeks.CalculateGreeks(put, CreateMarket(s_valuationDate));

        g.Delta.Should().BeInRange(-1d, 0d);
        g.Gamma.Should().BeGreaterThan(0d);
        g.Vega.Should().BeGreaterThan(0d);
        g.Rho.Should().BeLessThan(0d);     // put rho negative
    }

    [Fact]
    public void CalculateGreeks_AtmCallDelta_IsNearHalf()
    {
        var greeks = new BlackScholesGreeks();
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));

        GreekSet g = greeks.CalculateGreeks(call, CreateMarket(s_valuationDate, r: 0.0, q: 0.0));

        g.Delta.Should().BeApproximately(0.5d, 0.06d);
    }

    [Fact]
    public void Theta_IsPerCalendarDay_MatchesOneDayReprice()
    {
        var pricer = new BlackScholesPricer();
        var greeks = new BlackScholesGreeks();
        var expiry = s_valuationDate.AddMonths(6);
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);

        double priceToday = (double)pricer.Price(call, CreateMarket(s_valuationDate)).Price.Amount;
        double priceTomorrow = (double)pricer.Price(call, CreateMarket(s_valuationDate.AddDays(1))).Price.Amount;
        double thetaPerDay = greeks.CalculateGreeks(call, CreateMarket(s_valuationDate)).Theta!.Value;

        // Theta (per calendar day) should match the one-day finite-difference decay.
        thetaPerDay.Should().BeApproximately(priceTomorrow - priceToday, 1e-3);
    }

    [Fact]
    public void CalculateGreeks_InvalidCallPut_Throws()
    {
        var greeks = new BlackScholesGreeks();
        Action act = () => greeks.CalculateGreeks(
            new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6)) with { CallPut = (CallPut)999 },
            CreateMarket(s_valuationDate));

        act.Should().Throw<Exception>().Where(error => error.GetType() == typeof(ArgumentOutOfRangeException) || error.GetType() == typeof(NotSupportedException));
    }

    [Fact]
    public void CalculateGreeks_Expired_ReturnsEmpty()
    {
        var greeks = new BlackScholesGreeks();
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate);

        GreekSet g = greeks.CalculateGreeks(call, CreateMarket(s_valuationDate));

        g.Should().Be(GreekSet.Empty);
    }
}
