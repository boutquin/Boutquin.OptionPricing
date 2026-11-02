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
using Boutquin.OptionPricing.Calibration.ImpliedVolatility;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Calibration.Tests;

public sealed class ImpliedVolatilitySolverTests
{
    [Fact]
    public void Solve_ShouldRecoverFlatVolatility()
    {
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);
        decimal observedPrice = new BlackScholesPricer().Price(option, market).Price.Amount;

        var request = new ImpliedVolatilityRequest(option, market.WithFlatVolatility(0.10d), observedPrice, 0.20d);
        var result = new ImpliedVolatilitySolver().Solve(request);

        result.IsSuccess.Should().BeTrue();
        result.ImpliedVolatility.Should().NotBeNull();
        result.ImpliedVolatility!.Value.Should().BeApproximately(0.20d, 1e-4d);
    }

    [Fact]
    public void Solve_Put_ShouldRecoverFlatVolatility()
    {
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var option = new EuropeanOption(CallPut.Put, 100m, Currency.USD, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.25d, 0.03d, 0.01d);
        decimal observedPrice = new BlackScholesPricer().Price(option, market).Price.Amount;

        var request = new ImpliedVolatilityRequest(option, market.WithFlatVolatility(0.15d), observedPrice, 0.15d);
        var result = new ImpliedVolatilitySolver().Solve(request);

        result.IsSuccess.Should().BeTrue();
        result.ImpliedVolatility.Should().NotBeNull();
        result.ImpliedVolatility!.Value.Should().BeApproximately(0.25d, 1e-4d);
    }

    [Fact]
    public void Solve_DeepInTheMoney_ShouldConverge()
    {
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 150m, 0.30d, 0.03d, 0.01d);
        decimal observedPrice = new BlackScholesPricer().Price(option, market).Price.Amount;

        var request = new ImpliedVolatilityRequest(option, market.WithFlatVolatility(0.15d), observedPrice, 0.20d);
        var result = new ImpliedVolatilitySolver().Solve(request);

        result.IsSuccess.Should().BeTrue();
        result.ImpliedVolatility!.Value.Should().BeApproximately(0.30d, 1e-4d);
    }

    [Fact]
    public void Solve_DeepOutOfTheMoney_ShouldConverge()
    {
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 50m, 0.30d, 0.03d, 0.01d);
        decimal observedPrice = new BlackScholesPricer().Price(option, market).Price.Amount;

        var request = new ImpliedVolatilityRequest(option, market.WithFlatVolatility(0.15d), observedPrice, 0.20d);
        var result = new ImpliedVolatilitySolver().Solve(request);

        result.IsSuccess.Should().BeTrue();
        result.ImpliedVolatility!.Value.Should().BeApproximately(0.30d, 1e-4d);
    }

    [Fact]
    public void Solve_AmericanOption_ShouldReturnUnsupported()
    {
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var option = new AmericanOption(CallPut.Put, 100m, Currency.USD, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);

        var request = new ImpliedVolatilityRequest(option, market, 5.0m, 0.20d);
        var result = new ImpliedVolatilitySolver().Solve(request);

        result.Status.Should().Be(CalculationStatus.UnsupportedScenario);
        result.ImpliedVolatility.Should().BeNull();
    }

    [Fact]
    public void Solve_ZeroObservedPrice_ShouldNotConverge()
    {
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);

        // Price of 0 means the objective is always negative in the bracket — no root
        var request = new ImpliedVolatilityRequest(option, market.WithFlatVolatility(0.15d), 0m, 0.15d);
        var result = new ImpliedVolatilitySolver().Solve(request);

        result.IsSuccess.Should().BeFalse();
    }

    // ── Finding 006: input guards on Solve ───────────────────────────────────────

    [Fact]
    public void Solve_NullRequest_ShouldThrowArgumentNullException()
    {
        // Bare NRE on null today (no ArgumentNullException.ThrowIfNull guard).
        // After the fix a clear ArgumentNullException must be thrown.
        var solver = new ImpliedVolatilitySolver();
        Action act = () => solver.Solve(null!);
        act.Should().Throw<ArgumentNullException>(
            because: "request is a mandatory parameter; null must throw ArgumentNullException");
    }

    [Fact]
    public void Solve_ZeroFunctionTolerance_ShouldThrowArgumentOutOfRangeException()
    {
        // Zero tolerance passes Math.Min(0, BracketTol) = 0 to BrentSolver, which has
        // undefined behavior. The fix must reject it before reaching the solver.
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);

        var request = new ImpliedVolatilityRequest(
            option, market, 5.0m, InitialGuess: 0.20d,
            FunctionTolerance: 0.0, BracketTolerance: 1e-8);

        var solver = new ImpliedVolatilitySolver();
        Action act = () => solver.Solve(request);
        act.Should().Throw<ArgumentOutOfRangeException>(
            because: "FunctionTolerance = 0 is not positive and must be rejected");
    }

    [Fact]
    public void Solve_NegativeBracketTolerance_ShouldThrowArgumentOutOfRangeException()
    {
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);

        var request = new ImpliedVolatilityRequest(
            option, market, 5.0m, InitialGuess: 0.20d,
            FunctionTolerance: 1e-8, BracketTolerance: -1e-4);

        var solver = new ImpliedVolatilitySolver();
        Action act = () => solver.Solve(request);
        act.Should().Throw<ArgumentOutOfRangeException>(
            because: "BracketTolerance = -1e-4 is not positive and must be rejected");
    }

    [Fact]
    public void Solve_NonFiniteInitialGuess_ShouldThrowArgumentOutOfRangeException()
    {
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);

        var request = new ImpliedVolatilityRequest(
            option, market, 5.0m, InitialGuess: double.NaN);

        var solver = new ImpliedVolatilitySolver();
        Action act = () => solver.Solve(request);
        act.Should().Throw<ArgumentOutOfRangeException>(
            because: "InitialGuess = NaN is non-finite and must be rejected");
    }

    [Fact]
    public void Solve_ValidRequestAndTolerances_DoNotRejectHappyPath()
    {
        // A well-formed request with valid tolerances still succeeds
        // (regression guard ensuring guards don't block the happy path).
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
        var market = CreateMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);
        decimal observedPrice = new BlackScholesPricer().Price(option, market).Price.Amount;

        var request = new ImpliedVolatilityRequest(
            option, market.WithFlatVolatility(0.10d), observedPrice, 0.20d,
            FunctionTolerance: 1e-8, BracketTolerance: 1e-8);

        var solver = new ImpliedVolatilitySolver();
        Action act = () => solver.Solve(request);
        act.Should().NotThrow(because: "valid tolerances and finite InitialGuess must not be rejected");
    }

    // AC-5b — characterization test: ImpliedVolatilitySolver.Solve propagates
    // CurrencyMismatchException when the option currency (USD) differs from the market spot
    // currency (EUR). Inside Solve(), the objective lambda calls BlackScholesPricer.Price()
    // which carries the currency boundary guard; CurrencyMismatchException extends Exception
    // (not InvalidOperationException), so it escapes the catch block on line ~168 and
    // propagates to the caller. This pins that Solve() can raise the exception, warranting
    // the <exception cref="CurrencyMismatchException"/> doc tag (DOC-CCYMISMATCH-IVSOLVE-008).
    [Fact]
    public void Solve_CrossCurrencySpotVsOption_ThrowsCurrencyMismatchException()
    {
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var usdOption = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
        var eurMarket = new OptionMarketData(
            valuationDate,
            Currency.EUR.Amount(100m),
            0.20d,
            new FlatDiscountCurve(new CurveName("EUR-OIS"), valuationDate, Currency.EUR, 0.03d),
            new FlatDiscountCurve(new CurveName("DIVIDEND"), valuationDate, Currency.EUR, 0.01d));
        var request = new ImpliedVolatilityRequest(usdOption, eurMarket, 10m, 0.20d);

        Action act = () => new ImpliedVolatilitySolver().Solve(request);

        act.Should().Throw<CurrencyMismatchException>();
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
