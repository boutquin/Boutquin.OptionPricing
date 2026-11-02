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
using Boutquin.Numerics.Random;
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Analytics.Hedging;
using Boutquin.OptionPricing.Analytics.Parity;
using Boutquin.OptionPricing.Analytics.StochasticVolatility;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Analytics.Tests;

public sealed class RcRobustnessTests
{
    private static readonly DateOnly s_today = new(2026, 1, 2);

    private static OptionMarketData Market(double q = 0d, double b = 0d, Currency ccy = Currency.USD)
        => new(s_today, ccy.Amount(100m), 0.2d,
            new FlatDiscountCurve(new CurveName("R"), s_today, ccy, 0.05d),
            new FlatDiscountCurve(new CurveName("Q"), s_today, ccy, q),
            new FlatDiscountCurve(new CurveName("B"), s_today, ccy, b));

    [Fact]
    public void LargeFiniteVolatility_PreservesTheBlackScholesLimits()
    {
        var market = Market() with { FlatVolatility = 1e200d };
        var expiry = s_today.AddDays(365);
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);
        var put = new EuropeanOption(CallPut.Put, 100m, Currency.USD, expiry);
        var pricer = new BlackScholesPricer();
        ((double)pricer.Price(call, market).Price.Amount).Should().BeApproximately(100d, 1e-10d);
        ((double)pricer.Price(put, market).Price.Amount).Should().BeApproximately(100d * Math.Exp(-0.05d), 1e-10d);
    }

    [Fact]
    public void Hedge_UnrepresentableInceptionPrice_FailsBeforeProducingAResult()
    {
        var market = Market() with { Spot = Currency.USD.Amount(decimal.MaxValue) };
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_today.AddDays(365));
        FluentActions.Invoking(() => new DeltaHedgeSimulator().Simulate(new(call, market, new[] { decimal.MaxValue, decimal.MaxValue })))
            .Should().Throw<ArithmeticException>();
    }

    [Fact]
    public void Heston_CurrencyGuard_PrecedesSimulation()
    {
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_today.AddDays(365));
        var pricer = new SimpleHestonMcPricer(new HestonParameters(0.04d, 1d, 0.04d, 0.2d, 0d), 1, 1, new Pcg64RandomSource(1));
        FluentActions.Invoking(() => pricer.Price(call, Market(ccy: Currency.EUR))).Should().Throw<CurrencyMismatchException>();
    }

    [Fact]
    public void RawParity_UnrepresentablePrice_HasNumericalContext()
    {
        FluentActions.Invoking(() => PutCallParity.ImpliedCallPrice(5m, 100m, 100m, 1d, 1e30d))
            .Should().Throw<ArithmeticException>().WithMessage("*parity*decimal*");
    }

    [Fact]
    public void CurveParity_ConsumesBorrowInBothDirections()
    {
        var market = Market(0.02d, 0.03d);
        var expiry = s_today.AddDays(365);
        // Algebraic carry identity, independent of any option pricing implementation.
        decimal carry = (decimal)(100d * Math.Exp(-0.05d) - 100d * Math.Exp(-0.05d));
        PutCallParity.ImpliedCallPrice(7m, 100m, 100m, market, expiry).Should().BeApproximately(7m + carry, 1e-10m);
        PutCallParity.ImpliedPutPrice(7m, 100m, 100m, market, expiry).Should().BeApproximately(7m - carry, 1e-10m);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void RawForward_RejectsInvalidFactorsInEveryPosition(double value)
    {
        FluentActions.Invoking(() => RiskNeutralValuation.ForwardPrice(value, 1d)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => RiskNeutralValuation.ForwardPrice(100d, value)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => RiskNeutralValuation.ForwardPrice(100d, 1d, value)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => RiskNeutralValuation.ForwardPrice(100d, 1d, 1d, value)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RawForward_UnrepresentableResult_FailsExplicitly()
        => FluentActions.Invoking(() => RiskNeutralValuation.ForwardPrice(double.MaxValue, double.Epsilon)).Should().Throw<ArithmeticException>();

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void RawParity_RejectsInvalidDiscountFactors(double factor)
    {
        FluentActions.Invoking(() => PutCallParity.ImpliedCallPrice(5m, 100m, 100m, factor)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => PutCallParity.ImpliedPutPrice(5m, 100m, 100m, 1d, factor)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Parity_ApproximatePredicate_HasDiscriminatingTolerance()
    {
        PutCallParity.IsApproximatelySatisfied(5m, 5m, 100m, 100m, 1d, 0m).Should().BeTrue();
        PutCallParity.IsApproximatelySatisfied(5.01m, 5m, 100m, 100m, 1d, 0.001m).Should().BeFalse();
        FluentActions.Invoking(() => PutCallParity.IsApproximatelySatisfied(5m, 5m, 100m, 100m, 1d, -1m)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Greeks_RejectsCurrencyMismatchAndNulls()
    {
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_today.AddDays(365));
        var provider = new BlackScholesGreeks();
        FluentActions.Invoking(() => provider.CalculateGreeks(call, Market(ccy: Currency.EUR))).Should().Throw<CurrencyMismatchException>();
        FluentActions.Invoking(() => provider.CalculateGreeks(null!, Market())).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => provider.CalculateGreeks(call, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new BlackScholesPricer().Price(null!, Market())).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new BlackScholesPricer().Price(call, null!)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Heston_RejectsEveryNonfiniteParameter(double value)
    {
        var ok = new HestonParameters(0.04d, 1d, 0.04d, 0.2d, 0d);
        foreach (var parameters in new[] { ok with { V0 = value }, ok with { Kappa = value }, ok with { Theta = value }, ok with { SigmaV = value }, ok with { Rho = value } })
        {
            FluentActions.Invoking(() => new SimpleHestonMcPricer(parameters, 1, 1, new Pcg64RandomSource(1))).Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Fact]
    public void Heston_ExpiredPutAndNonFellerRegime_HaveExplicitResults()
    {
        var parameters = new HestonParameters(0.04d, 0.1d, 0.04d, 0.5d, -0.5d);
        var pricer = new SimpleHestonMcPricer(parameters, 32, 8, new Pcg64RandomSource(1));
        var expired = pricer.Price(new EuropeanOption(CallPut.Put, 110m, Currency.USD, s_today), Market());
        expired.Price.Amount.Should().Be(10m);
        var live = pricer.Price(new EuropeanOption(CallPut.Put, 110m, Currency.USD, s_today.AddDays(30)), Market());
        live.IsSuccess.Should().BeTrue();
        live.GetDiagnostics()["fellerSatisfied"].Should().Be(false);
    }

    [Fact]
    public void Heston_FiniteButUnrepresentableDynamics_ReturnsUnsupported()
    {
        var parameters = new HestonParameters(0.04d, 1e308d, 1e308d, 0d, 0d);
        var pricer = new SimpleHestonMcPricer(parameters, 1, 2, new Pcg64RandomSource(1));
        var result = pricer.Price(new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_today.AddDays(365)), Market());
        result.Status.Should().Be(CalculationStatus.UnsupportedScenario);
        result.Price.Amount.Should().Be(0m);
    }

    [Fact]
    public void Hedge_ExpiredPathAndInvalidSpots_AreRejectedBeforeRolling()
    {
        var simulator = new DeltaHedgeSimulator();
        var live = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_today.AddDays(365));
        var expired = live with { ExpiryDate = s_today };
        FluentActions.Invoking(() => simulator.Simulate(new(expired, Market(), new[] { 100m, 100m, 100m }))).Should().Throw<ArgumentException>().WithMessage("*expired*");
        foreach (var path in new[] { new[] { 99m, 100m }, new[] { 100m, 0m }, new[] { 100m, -1m } })
        {
            FluentActions.Invoking(() => simulator.Simulate(new(live, Market(), path))).Should().Throw<ArgumentException>();
        }
        FluentActions.Invoking(() => simulator.Simulate(null!)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0.05d, 0d)]
    [InlineData(0d, 0.05d)]
    [InlineData(0.02d, 0.03d)]
    public void Hedge_ReinvestedCarry_ReplicatesDeepItmCall(double q, double b)
    {
        // For a forward-like call, delta=e^(-(q+b)T). Reinvesting the yield
        // grows that holding to one share; omitting income loses about five dollars.
        var call = new EuropeanOption(CallPut.Call, 1m, Currency.USD, s_today.AddDays(365));
        var path = Enumerable.Repeat(100m, 366).ToArray();
        var result = new DeltaHedgeSimulator().Simulate(new(call, Market(q, b), path));
        result.OptionPayoff.Should().Be(99m);
        result.HedgingError.Should().BeApproximately(0m, 1e-8m);
    }

    [Fact]
    public void Hedge_ResultOwnsItsPathSnapshot()
    {
        var path = new[] { 100m, 110m };
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_today.AddDays(365));
        var result = new DeltaHedgeSimulator().Simulate(new(call, Market(), path));
        path[1] = 1m;
        result.Spots[1].Should().Be(110m);
        result.OptionPayoff.Should().Be(10m);
    }

    [Theory]
    [InlineData(CallPut.Call)]
    [InlineData(CallPut.Put)]
    public void Merton_PutAndCall_MatchIndependentScipyValues(CallPut side)
    {
        // Independent erfc normal CDF; S*=100-1.6*exp(-.05*180/365), sigma*=.2*100/S*.
        var schedule = new DividendSchedule(new[] { new DividendPayment(s_today.AddDays(180), 1.6m) });
        var option = new EuropeanOption(side, 100m, Currency.USD, s_today.AddDays(365));
        double expected = side == CallPut.Call ? 9.599719972104793 : 6.283692789331781;
        ((double)new MertonDiscreteDividendPricer(schedule).Price(option, Market()).Price.Amount).Should().BeApproximately(expected, 1e-6d);
    }
}
