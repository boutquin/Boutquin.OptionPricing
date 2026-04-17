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
using Boutquin.Curves.Abstractions.Identifiers;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.Curves.Bootstrap;
using Boutquin.Curves.Bootstrap.ReferenceData;
using Boutquin.MarketData.Conventions;
using Boutquin.Curves.Core;
using Boutquin.Curves.Core.Discounting;
using Boutquin.Curves.Recipes;
using Boutquin.Curves.Recipes.Testing;
using Boutquin.MarketData.Calendars.Holidays;
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Boutquin.OptionPricing.IntegrationTests;

public sealed class MarketIntegrationExampleTests
{
    private static readonly DateOnly s_valuationDate = FixtureData.FixtureDate;
    private const CurrencyCode s_usd = CurrencyCode.USD;

    [Fact]
    public async Task TermStructureGreeks_ShouldDifferFromFlatGreeks()
    {
        // Flat-curve market
        var flatDiscount = new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, s_usd, 0.03);
        var flatMarket = new OptionMarketData(s_valuationDate, 100m, 0.20, flatDiscount);

        // Bootstrapped-curve market
        var snapshot = await BuildUsdSnapshotAsync().ConfigureAwait(true);
        var discountRef = new CurveReference(CurveRole.Discount, s_usd, new BenchmarkName("SOFR"));
        var bootstrappedDiscount = (IDiscountCurve)snapshot.CurveGroup.GetCurve(discountRef);
        var bootstrappedMarket = new OptionMarketData(s_valuationDate, 100m, 0.20, bootstrappedDiscount);

        var option = new EuropeanOption(CallPut.Call, 100m, s_valuationDate.AddMonths(6));
        var greeks = new BlackScholesGreeks();

        var flatGreeks = greeks.CalculateGreeks(option, flatMarket);
        var tsGreeks = greeks.CalculateGreeks(option, bootstrappedMarket);

        // Rho should differ because the term structures are different
        flatGreeks.Rho.Should().NotBeApproximately(tsGreeks.Rho, 1e-6,
            "term-structure rho should differ from flat rho");
    }

    [Fact]
    public async Task FromCurveGroup_ShouldPriceIdenticallyToManualCurveExtraction()
    {
        var snapshot = await BuildUsdSnapshotAsync().ConfigureAwait(true);

        // Manual extraction
        var discountRef = new CurveReference(CurveRole.Discount, s_usd, new BenchmarkName("SOFR"));
        var discountCurve = (IDiscountCurve)snapshot.CurveGroup.GetCurve(discountRef);
        var manualMarket = new OptionMarketData(s_valuationDate, 100m, 0.20, discountCurve);

        // Factory
        var factoryMarket = OptionMarketData.FromCurveGroup(snapshot.CurveGroup, s_usd, 100m, 0.20, new BenchmarkName("SOFR"));

        var option = new EuropeanOption(CallPut.Call, 100m, s_valuationDate.AddMonths(6));
        var pricer = new BlackScholesPricer();

        var manualPrice = pricer.Price(option, manualMarket).Price;
        var factoryPrice = pricer.Price(option, factoryMarket).Price;

        factoryPrice.Should().Be(manualPrice);
    }

    [Fact]
    public void MultiCurveDiscounting_ShouldAffectForwardPrice_WhenBorrowCurveAdded()
    {
        var discountCurve = new FlatDiscountCurve(new CurveName("USD-DISC"), s_valuationDate, s_usd, 0.05);
        var borrowCurve = new FlatDiscountCurve(new CurveName("BORROW"), s_valuationDate, s_usd, 0.02);
        var expiryDate = s_valuationDate.AddMonths(6);

        // Without borrow
        var groupNoBorrow = new CurveGroupBuilder(new CurveGroupName("NO-BORROW"), s_valuationDate)
            .Add(new CurveReference(CurveRole.Discount, s_usd), discountCurve)
            .Build();
        var marketNoBorrow = OptionMarketData.FromCurveGroup(groupNoBorrow, s_usd, 100m, 0.20);

        // With borrow
        var groupWithBorrow = new CurveGroupBuilder(new CurveGroupName("WITH-BORROW"), s_valuationDate)
            .Add(new CurveReference(CurveRole.Discount, s_usd), discountCurve)
            .Add(new CurveReference(CurveRole.Borrow, s_usd), borrowCurve)
            .Build();
        var marketWithBorrow = OptionMarketData.FromCurveGroup(groupWithBorrow, s_usd, 100m, 0.20);

        // Borrow curve affects forward price via cost-of-carry formula
        var fwdNoBorrow = marketNoBorrow.ForwardPrice(expiryDate);
        var fwdWithBorrow = marketWithBorrow.ForwardPrice(expiryDate);

        fwdWithBorrow.Should().NotBeApproximately(fwdNoBorrow, 1e-6,
            "borrow cost should change the cost-of-carry forward price");
        marketWithBorrow.BorrowRate(expiryDate).Should().BeApproximately(0.02, 1e-4);
    }

    [Fact]
    public async Task DiagnosticGatedPricing_ShouldPassWithFixtureData()
    {
        var snapshot = await BuildUsdSnapshotAsync().ConfigureAwait(true);

        // Fixture data should produce tight repricing
        double maxError = snapshot.Diagnostics.Repricing.Count > 0
            ? snapshot.Diagnostics.Repricing.Max(r => r.AbsoluteError)
            : 0d;

        maxError.Should().BeLessThan(1e-6, "fixture data should bootstrap cleanly");

        // Only price if diagnostics pass
        var market = OptionMarketData.FromCurveGroup(snapshot.CurveGroup, s_usd, 100m, 0.20, new BenchmarkName("SOFR"));
        var option = new EuropeanOption(CallPut.Call, 100m, s_valuationDate.AddMonths(6));
        var result = new BlackScholesPricer().Price(option, market);

        result.IsSuccess.Should().BeTrue();
        result.Price.Should().BeGreaterThan(0m);
    }

    private static async Task<CurveSnapshot> BuildUsdSnapshotAsync()
    {
        var pipeline = FixtureData.CreatePipeline();
        var calibrator = new PiecewiseBootstrapCalibrator();
        var referenceData = CreateReferenceData();
        var logger = NullLoggerFactory.Instance.CreateLogger<CurveBuilder>();
        var builder = new CurveBuilder(pipeline, calibrator, referenceData, logger);

        var recipe = StandardCurveRecipes.UsdSofrDiscount(calendar: HolidayCalendarFactory.Create("USNY"));
        return await builder.BuildAsync(recipe, s_valuationDate).ConfigureAwait(true);
    }

    private static SimpleReferenceDataProvider CreateReferenceData()
    {
        var calendars = HolidayCalendarFactory.SupportedCodes.Select(HolidayCalendarFactory.Create).ToList();
        var benchmarks = new[]
        {
            new RateBenchmark(new BenchmarkName("SOFR"), CurrencyCode.USD, BenchmarkKind.OvernightRiskFree, null, 0, 1, "USNY", "ACT/360", true),
        };
        var registry = InstrumentConventionRegistry.CreateDefault();
        var conventions = new[]
        {
            registry.GetRequired("USD-SOFR-OIS"),
            registry.GetRequired("USD-FIXED-6M-30-360"),
        };
        return new SimpleReferenceDataProvider(calendars, benchmarks, conventions);
    }
}
