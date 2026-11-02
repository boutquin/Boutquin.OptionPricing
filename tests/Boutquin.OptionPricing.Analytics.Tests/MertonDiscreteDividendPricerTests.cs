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
using Boutquin.Domain.Exceptions;
using Boutquin.Domain.Extensions;
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Analytics.Tests;

public sealed class MertonDiscreteDividendPricerTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 15);

    [Fact]
    public void EmptySchedule_EqualsBlackScholesPricer_ToOneInTwelve()
    {
        // AC-1: with no discrete dividends (or all ex-dates >= T), PV(divs) = 0, so
        // S* = S0 and sigma* = sigma, and the Merton pricer must reduce exactly to the
        // plain Black-Scholes price on the unadjusted inputs.
        var market = CreateMarket(spot: 100m, flatVol: 0.20d, riskFreeRate: 0.05d);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddDays(365));

        var bsPrice = new BlackScholesPricer().Price(option, market).Price;
        var mertonResult = new MertonDiscreteDividendPricer(DividendSchedule.Empty).Price(option, market);

        mertonResult.Status.Should().Be(CalculationStatus.Success);
        ((double)mertonResult.Price.Amount).Should().BeApproximately((double)bsPrice.Amount, 1e-12);
    }

    [Fact]
    public void AllExDatesAtOrAfterExpiry_EqualsBlackScholesPricer()
    {
        // AC-1 (boundary): dividends whose ex-dates fall on or after expiry are NOT
        // escrowed (the holder of the option does not capture them), so PV(divs) = 0
        // and the result still reduces to Black-Scholes.
        var market = CreateMarket(spot: 100m, flatVol: 0.20d, riskFreeRate: 0.05d);
        var expiry = s_valuationDate.AddDays(365);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);
        var schedule = new DividendSchedule(new[]
        {
            new DividendPayment(expiry, 1.50m),               // exactly at expiry: excluded
            new DividendPayment(expiry.AddDays(30), 2.00m),   // after expiry: excluded
        });

        var bsPrice = new BlackScholesPricer().Price(option, market).Price;
        var mertonResult = new MertonDiscreteDividendPricer(schedule).Price(option, market);

        mertonResult.Status.Should().Be(CalculationStatus.Success);
        ((double)mertonResult.Price.Amount).Should().BeApproximately((double)bsPrice.Amount, 1e-12);
    }

    [Fact]
    public void ChrissExample_MatchesEscrowedSpotModel_WithinTolerance()
    {
        // AC-2: Chriss "Black-Scholes and Beyond" sec 4.9 discrete-dividend example.
        // S = 100, K = 100, two $0.80 cash dividends paid before expiry. The escrowed
        // model removes the present value of those dividends from the stochastic spot
        // and uplifts the volatility so the *stochastic* base S* carries the original
        // instantaneous dollar volatility:
        //     PV(divs) = sum_i d_i * DF(t_i)
        //     S*       = S0 - PV(divs)
        //     sigma*   = (S0 / S*) * sigma
        // The expected price is therefore the Black-Scholes price evaluated on (S*, sigma*).
        // We derive that expected value here from the same arithmetic (so the assertion
        // pins the model, not a transcribed book figure that could embed a different
        // day-count or compounding convention), and assert the pricer reproduces it.
        var market = CreateMarket(spot: 100m, flatVol: 0.20d, riskFreeRate: 0.06d);
        var expiry = s_valuationDate.AddDays(365);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);

        var exDate1 = s_valuationDate.AddDays(90);
        var exDate2 = s_valuationDate.AddDays(270);
        var schedule = new DividendSchedule(new[]
        {
            new DividendPayment(exDate1, 0.80m),
            new DividendPayment(exDate2, 0.80m),
        });

        // Independent escrowed-model arithmetic (the oracle).
        double pvDivs = (0.80d * market.DiscountFactor(exDate1)) + (0.80d * market.DiscountFactor(exDate2));
        double sStar = 100d - pvDivs;
        double sigmaStar = (100d / sStar) * 0.20d;
        var escrowedMarket = new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount((decimal)sStar),
            sigmaStar,
            market.DiscountCurve);
        double expected = (double)new BlackScholesPricer().Price(option, escrowedMarket).Price.Amount;

        var mertonResult = new MertonDiscreteDividendPricer(schedule).Price(option, market);

        mertonResult.Status.Should().Be(CalculationStatus.Success);
        ((double)mertonResult.Price.Amount).Should().BeApproximately(expected, 1e-9);

        // Diagnostics expose the escrowed intermediates for audit.
        var diag = mertonResult.GetDiagnostics();
        ((double)diag["pvDividends"]!).Should().BeApproximately(pvDivs, 1e-12);
        ((double)diag["escrowedSpot"]!).Should().BeApproximately(sStar, 1e-12);
        ((double)diag["upliftedVolatility"]!).Should().BeApproximately(sigmaStar, 1e-12);
        mertonResult.ModelName.Should().Be("Merton-DiscreteDividend");
        mertonResult.AssumptionSummary.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void PositivePvDividends_UpliftsVolatilityStrictly()
    {
        // AC-3: whenever PV(divs) > 0, the stochastic base S* < S0, so the volatility
        // uplift sigma* = (S0 / S*) * sigma is strictly greater than sigma.
        var market = CreateMarket(spot: 100m, flatVol: 0.20d, riskFreeRate: 0.05d);
        var expiry = s_valuationDate.AddDays(365);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);
        var schedule = new DividendSchedule(new[]
        {
            new DividendPayment(s_valuationDate.AddDays(90), 1.25m),
            new DividendPayment(s_valuationDate.AddDays(270), 1.25m),
        });

        var result = new MertonDiscreteDividendPricer(schedule).Price(option, market);
        var diag = result.GetDiagnostics();

        ((double)diag["pvDividends"]!).Should().BeGreaterThan(0d);
        ((double)diag["upliftedVolatility"]!).Should().BeGreaterThan(0.20d);
    }

    [Fact]
    public void DividendsExceedingSpot_ReturnsUnsupportedScenario()
    {
        // AC-4: when the present value of dividends meets or exceeds the spot, the
        // escrowed spot S* would be non-positive — the model is undefined. The pricer
        // returns an UnsupportedScenario result for "Merton-DiscreteDividend" rather
        // than throwing or producing a nonsense price.
        var market = CreateMarket(spot: 100m, flatVol: 0.20d, riskFreeRate: 0.05d);
        var expiry = s_valuationDate.AddDays(365);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);
        var schedule = new DividendSchedule(new[]
        {
            new DividendPayment(s_valuationDate.AddDays(90), 60m),
            new DividendPayment(s_valuationDate.AddDays(270), 60m),
        });

        var result = new MertonDiscreteDividendPricer(schedule).Price(option, market);

        result.Status.Should().Be(CalculationStatus.UnsupportedScenario);
        result.IsSuccess.Should().BeFalse();
        result.Price.Should().Be(Currency.USD.Amount(0m));
        result.ModelName.Should().Be("Merton-DiscreteDividend");
    }

    [Fact]
    public void PastExDate_NotCountedInDividendCountDiagnostic()
    {
        // F3 regression: CountCapturedDividends previously filtered only on
        // ExDate < expiryDate, without the same >= ValuationDate lower bound
        // that PresentValueOfDividends uses. A dividend with an ex-date before
        // the valuation date contributes zero present value (the holder already
        // went ex) but was still counted in the 'dividendCount' diagnostic —
        // making the count inconsistent with the PV it accompanies.
        //
        // Arrange: one past ex-date (before valuation date) and one future
        // ex-date (in [valuationDate, expiryDate)).
        var market = CreateMarket(spot: 100m, flatVol: 0.20d, riskFreeRate: 0.05d);
        var expiry = s_valuationDate.AddDays(365);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);
        var schedule = new DividendSchedule(new[]
        {
            new DividendPayment(s_valuationDate.AddDays(-30), 0.80m), // past: contributes zero PV
            new DividendPayment(s_valuationDate.AddDays( 90), 0.80m), // future: contributes to PV
        });

        var result = new MertonDiscreteDividendPricer(schedule).Price(option, market);
        var diag = result.GetDiagnostics();

        // Only the future dividend is PV-contributing, so dividendCount must be 1.
        ((int)diag["dividendCount"]!).Should().Be(1,
            because: "only dividends with ex-date >= ValuationDate contribute to PV(divs)");
        // PV(divs) should reflect only the future dividend (non-zero).
        ((double)diag["pvDividends"]!).Should().BeGreaterThan(0d);
    }

    // AC-5b — characterization test: MertonDiscreteDividendPricer.Price throws
    // CurrencyMismatchException when option currency (USD) differs from market spot
    // currency (EUR). This pins that the public entry point can raise the exception,
    // warranting the <exception cref="CurrencyMismatchException"/> doc tag (DOC-CCYMISMATCH-MERTON-005).
    [Fact]
    public void Price_CrossCurrencySpotVsOption_ThrowsCurrencyMismatchException()
    {
        var usdOption = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddDays(365));
        var eurMarket = new OptionMarketData(
            s_valuationDate,
            Currency.EUR.Amount(100m),
            0.20d,
            new FlatDiscountCurve(new CurveName("EUR-OIS"), s_valuationDate, Currency.EUR, 0.03d));

        Action act = () => new MertonDiscreteDividendPricer(DividendSchedule.Empty).Price(usdOption, eurMarket);

        act.Should().Throw<CurrencyMismatchException>();
    }

    private static OptionMarketData CreateMarket(decimal spot, double flatVol, double riskFreeRate)
    {
        return new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(spot),
            flatVol,
            new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, Currency.USD, riskFreeRate));
    }
}
