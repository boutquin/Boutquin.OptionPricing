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
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Domain.Results;
using FluentAssertions;

namespace Boutquin.OptionPricing.Domain.Tests;

public sealed class RcDomainRegressionTests
{
    private static readonly DateOnly s_date = new(2026, 1, 1);
    private static OptionMarketData Market(double vol = 0.2) => new(s_date, Currency.USD.Amount(100m), vol,
        new FlatDiscountCurve(new CurveName("USD"), s_date, Currency.USD, 0.05));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Market_RejectsNonFiniteVolOnAllPaths(double vol)
    {
        Action ctor = () => Market(vol);
        Action with = () => _ = Market() with { FlatVolatility = vol };
        Action helper = () => Market().WithFlatVolatility(vol);
        ctor.Should().Throw<ArgumentOutOfRangeException>();
        with.Should().Throw<ArgumentOutOfRangeException>();
        helper.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Market_RejectsNullCurveOnWithPath()
    {
        Action act = () => _ = Market() with { DiscountCurve = null! };
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Contract_RejectsNegativeSettlementLagOnConstructorAndWith()
    {
        Action ctor = () => _ = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_date, settlementLagDays: -1);
        Action with = () => _ = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_date) with { SettlementLagDays = -1 };
        ctor.Should().Throw<ArgumentOutOfRangeException>();
        with.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Barrier_RejectsUndefinedKnockTypeBeforeItCanBePricedAsKnockIn()
    {
        Action act = () => _ = new BarrierOption(CallPut.Call, 100m, Currency.USD, 120m, BarrierDirection.Up, (BarrierKnockType)99, s_date);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ResultDiagnostics_AreCopiedOnConstructorAndWithPaths()
    {
        var source = new Dictionary<string, object?> { ["x"] = 1 };
        var result = new PriceResult(Currency.USD.Amount(1m), Diagnostics: source);
        var copy = result with { Diagnostics = source };
        source["x"] = 2;
        result.GetDiagnostics()["x"].Should().Be(1);
        copy.GetDiagnostics()["x"].Should().Be(1);
    }

    [Fact]
    public void DividendSchedule_WithPathCopiesAndReturnCannotBeCastToMutableArray()
    {
        var source = new[] { new DividendPayment(s_date.AddDays(1), 1m) };
        var result = DividendSchedule.Empty with { Payments = source };
        source[0] = source[0] with { Amount = 2m };
        result.Payments[0].Amount.Should().Be(1m);
        Action mutate = () => ((IList<DividendPayment>)result.Payments)[0] = source[0];
        mutate.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void DividendSchedule_SortsChronologically()
    {
        var schedule = new DividendSchedule(new[] { new DividendPayment(s_date.AddDays(2), 2m), new DividendPayment(s_date.AddDays(1), 1m) });
        schedule.Payments.Select(x => x.ExDate).Should().BeInAscendingOrder();
    }

    [Fact]
    public void InvertYearFraction_RejectsNaN()
    {
        Action act = () => Market().InvertYearFraction(double.NaN);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
    [Fact]
    public void CurrencyAndRollingGuards_AreEnforcedAndValidRollUpdatesAllCurves()
    {
        var market = Market();
        Action with = () => _ = market with { Spot = Currency.CAD.Amount(100m) };
        with.Should().Throw<Boutquin.Domain.Exceptions.CurrencyMismatchException>();
        Action currency = () => market.RolledTo(s_date.AddDays(1), Currency.CAD.Amount(100m));
        currency.Should().Throw<Boutquin.Domain.Exceptions.CurrencyMismatchException>();
        Action amount = () => market.RolledTo(s_date.AddDays(1), Currency.USD.Amount(0m));
        amount.Should().Throw<ArgumentOutOfRangeException>();
        Action backward = () => market.RolledTo(s_date.AddDays(-1), Currency.USD.Amount(100m));
        backward.Should().Throw<ArgumentOutOfRangeException>();
        var rolled = market.RolledTo(s_date.AddDays(1), Currency.USD.Amount(110m));
        rolled.ValuationDate.Should().Be(s_date.AddDays(1));
        rolled.Spot.Amount.Should().Be(110m);
        rolled.DiscountCurve.ValuationDate.Should().Be(rolled.ValuationDate);
    }

    [Theory]
    [InlineData(CalculationStatus.Success, true)]
    [InlineData(CalculationStatus.ConvergedWithWarnings, true)]
    [InlineData(CalculationStatus.FailedToConverge, false)]
    [InlineData(CalculationStatus.InvalidInput, false)]
    [InlineData(CalculationStatus.UnsupportedScenario, false)]
    public void ResultSuccess_StatusContractIsExhaustive(CalculationStatus status, bool usable)
    {
        new PriceResult(Currency.CAD.Amount(1m), Status: status).IsSuccess.Should().Be(usable);
    }
    [Fact]
    public void UnsupportedResult_RequiresContractCurrencyAndPreservesReason()
    {
        Action invalid = () => PriceResult.Unsupported("model", "unsupported", Currency.Unspecified);
        invalid.Should().Throw<ArgumentException>();
        var result = PriceResult.Unsupported("model", "unsupported", Currency.CAD);
        result.Price.Currency.Should().Be(Currency.CAD);
        result.Price.Amount.Should().Be(0m);
        result.GetDiagnostics()["reason"].Should().Be("unsupported");
        result.IsSuccess.Should().BeFalse();
    }
}
