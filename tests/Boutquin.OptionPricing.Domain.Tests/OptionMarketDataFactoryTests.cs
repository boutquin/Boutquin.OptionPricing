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

using Boutquin.Curves.Abstractions.Curves;
using Boutquin.Curves.Abstractions.Environments;
using Boutquin.Curves.Abstractions.Identifiers;
using Boutquin.Curves.Core;
using Boutquin.Curves.Core.Discounting;
using Boutquin.Curves.Core.Forwards;
using Boutquin.Domain.Enumerations;
using Boutquin.Domain.Extensions;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Storage.InMemory;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Domain.Tests;

public sealed class OptionMarketDataFactoryTests
{
    private static readonly DateOnly s_valuationDate = new(2025, 6, 15);
    private const Currency Usd = Currency.USD;

    private static FlatDiscountCurve CreateDiscountCurve(double rate = 0.05)
    {
        return new FlatDiscountCurve(new CurveName("USD-DISC"), s_valuationDate, Usd, rate);
    }

    private static FlatDiscountCurve CreateDividendCurve(double rate = 0.02)
    {
        return new FlatDiscountCurve(new CurveName("DIVIDEND"), s_valuationDate, Usd, rate);
    }

    private static FlatDiscountCurve CreateBorrowCurve(double rate = 0.01)
    {
        return new FlatDiscountCurve(new CurveName("BORROW"), s_valuationDate, Usd, rate);
    }

    private static FlatForwardCurve CreateForwardCurve(double rate = 0.04)
    {
        return new FlatForwardCurve(new CurveName("USD-FWD"), s_valuationDate, Usd, new BenchmarkName("USD-SOFR"), rate);
    }

    // ── FromCurveGroup tests ──────────────────────────────────────────

    [Fact]
    public void FromCurveGroup_ShouldResolveDiscountCurve()
    {
        var discountCurve = CreateDiscountCurve();
        var group = new CurveGroupBuilder(new CurveGroupName("TEST"), s_valuationDate)
            .Add(new CurveReference(CurveRole.Discount, Usd), discountCurve)
            .Build();

        var market = OptionMarketData.FromCurveGroup(group, Usd, 100m, 0.20);

        market.DiscountCurve.Should().BeSameAs(discountCurve);
        market.ValuationDate.Should().Be(s_valuationDate);
        market.Spot.Should().Be(Usd.Amount(100m));
        market.FlatVolatility.Should().Be(0.20);
    }

    [Fact]
    public void FromCurveGroup_ShouldResolveDividendAndBorrowCurves()
    {
        var discountCurve = CreateDiscountCurve();
        var dividendCurve = CreateDividendCurve();
        var borrowCurve = CreateBorrowCurve();
        var group = new CurveGroupBuilder(new CurveGroupName("TEST"), s_valuationDate)
            .Add(new CurveReference(CurveRole.Discount, Usd), discountCurve)
            .Add(new CurveReference(CurveRole.Dividend, Usd), dividendCurve)
            .Add(new CurveReference(CurveRole.Borrow, Usd), borrowCurve)
            .Build();

        var market = OptionMarketData.FromCurveGroup(group, Usd, 100m, 0.20);

        market.DividendCurve.Should().BeSameAs(dividendCurve);
        market.BorrowCurve.Should().BeSameAs(borrowCurve);
    }

    [Fact]
    public void FromCurveGroup_ShouldResolveForwardCurve()
    {
        var discountCurve = CreateDiscountCurve();
        var forwardCurve = CreateForwardCurve();
        var group = new CurveGroupBuilder(new CurveGroupName("TEST"), s_valuationDate)
            .Add(new CurveReference(CurveRole.Discount, Usd), discountCurve)
            .Add(new CurveReference(CurveRole.Forward, Usd), forwardCurve)
            .Build();

        var market = OptionMarketData.FromCurveGroup(group, Usd, 100m, 0.20);

        market.ProjectionCurve.Should().BeSameAs(forwardCurve);
    }

    [Fact]
    public void FromCurveGroup_ShouldThrow_WhenNoDiscountCurve()
    {
        var forwardCurve = CreateForwardCurve();
        var group = new CurveGroupBuilder(new CurveGroupName("TEST"), s_valuationDate)
            .Add(new CurveReference(CurveRole.Forward, Usd), forwardCurve)
            .Build();

        var act = () => OptionMarketData.FromCurveGroup(group, Usd, 100m, 0.20);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FromCurveGroup_ShouldProduceIdenticalRates_AsManualConstruction()
    {
        var discountCurve = CreateDiscountCurve(0.05);
        var dividendCurve = CreateDividendCurve(0.02);
        var group = new CurveGroupBuilder(new CurveGroupName("TEST"), s_valuationDate)
            .Add(new CurveReference(CurveRole.Discount, Usd), discountCurve)
            .Add(new CurveReference(CurveRole.Dividend, Usd), dividendCurve)
            .Build();

        var fromFactory = OptionMarketData.FromCurveGroup(group, Usd, 100m, 0.20);
        var manual = new OptionMarketData(s_valuationDate, Currency.USD.Amount(100m), 0.20, discountCurve, dividendCurve);

        var expiryDate = s_valuationDate.AddMonths(6);
        fromFactory.RiskFreeRate(expiryDate).Should().BeApproximately(manual.RiskFreeRate(expiryDate), 1e-12);
        fromFactory.DividendYield(expiryDate).Should().BeApproximately(manual.DividendYield(expiryDate), 1e-12);
        fromFactory.ForwardPrice(expiryDate).Should().BeApproximately(manual.ForwardPrice(expiryDate), 1e-12);
    }

    // ── FromMarketEnvironment tests ───────────────────────────────────

    [Fact]
    public void FromMarketEnvironment_ShouldResolveAllCurves()
    {
        var discountCurve = CreateDiscountCurve();
        var dividendCurve = CreateDividendCurve();
        var curves = new Dictionary<CurveReference, ICurve>
        {
            [new CurveReference(CurveRole.Discount, Usd)] = discountCurve,
            [new CurveReference(CurveRole.Dividend, Usd)] = dividendCurve,
        };
        var env = new MarketEnvironment(s_valuationDate, curves, new Dictionary<BenchmarkName, RateBenchmark>(), new Dictionary<string, decimal>());

        var market = OptionMarketData.FromMarketEnvironment(env, Usd, 100m, 0.20);

        market.DiscountCurve.Should().BeSameAs(discountCurve);
        market.DividendCurve.Should().BeSameAs(dividendCurve);
        market.ValuationDate.Should().Be(s_valuationDate);
    }

    [Fact]
    public void FromMarketEnvironment_ShouldPassFixingsThrough()
    {
        var discountCurve = CreateDiscountCurve();
        var curves = new Dictionary<CurveReference, ICurve>
        {
            [new CurveReference(CurveRole.Discount, Usd)] = discountCurve,
        };
        var fixings = new Dictionary<string, decimal>
        {
            ["USD-SOFR"] = 0.043m,
        };
        var env = new MarketEnvironment(s_valuationDate, curves, new Dictionary<BenchmarkName, RateBenchmark>(), fixings);

        var market = OptionMarketData.FromMarketEnvironment(env, Usd, 100m, 0.20);

        market.FixingsStore.Should().NotBeNull();
        market.FixingsStore!.TryGetFixing(new BenchmarkName("USD-SOFR"), s_valuationDate, out decimal value).Should().BeTrue();
        value.Should().Be(0.043m);
    }

    // ── Fixings property tests ────────────────────────────────────────

    [Fact]
    public void FixingsStore_ShouldBeNull_ByDefault()
    {
        var market = new OptionMarketData(s_valuationDate, Currency.USD.Amount(100m), 0.20, CreateDiscountCurve());

        market.FixingsStore.Should().BeNull();
    }

    [Fact]
    public void FixingsStore_ShouldBeAccessible_WhenProvided()
    {
        var store = new InMemoryFixingsStore([(new BenchmarkName("USD-SOFR"), s_valuationDate, 0.043m)]);

        var market = new OptionMarketData(s_valuationDate, Currency.USD.Amount(100m), 0.20, CreateDiscountCurve(), fixingsStore: store);

        market.FixingsStore.Should().BeSameAs(store);
        market.FixingsStore!.GetFixing(new BenchmarkName("USD-SOFR"), s_valuationDate).Should().Be(0.043m);
    }
}
