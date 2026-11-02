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
using Boutquin.Curves.Bootstrap;
using Boutquin.Curves.Bootstrap.ReferenceData;
using Boutquin.Curves.Recipes;
using Boutquin.Curves.Recipes.Testing;
using Boutquin.Domain.Enumerations;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Calendars.Holidays;
using Boutquin.MarketData.Conventions;
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Calibration.ImpliedVolatility;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Examples.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Boutquin.OptionPricing.IntegrationTests;

/// <summary>
/// Integration tests that price options against bootstrapped curves from
/// <see cref="CurveBuilder"/> using fixture data, validating
/// that real term structure flows through the pricing pipeline correctly.
/// </summary>
public sealed class BootstrappedCurvePricingTests
{
    private static readonly DateOnly s_valuationDate = FixtureData.FixtureDate;
    private static readonly CurveReference s_usdDiscountRef = new(CurveRole.Discount, Currency.USD, new BenchmarkName("SOFR"));

    [Fact]
    public async Task BlackScholesPrice_WithBootstrappedCurve_ShouldRoundTripImpliedVol()
    {
        // Arrange — bootstrap USD SOFR discount from fixtures
        var snapshot = await BuildUsdSnapshotAsync().ConfigureAwait(true);

        // Keep the same native-rate quality bar as the executable example. Sparse coupon
        // dates must be repriced on the calibrated curve, not an earlier extrapolated tail.
        snapshot.Diagnostics.Repricing.Should().NotBeEmpty();
        snapshot.Diagnostics.Repricing.Should().AllSatisfy(item => item.AbsoluteError.Should().BeLessThan(1e-6));

        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var market = OptionMarketData.FromCurveGroup(
            snapshot.CurveGroup, Currency.USD, 100m, 0.20d, new BenchmarkName("SOFR"));

        // Act — price, then recover implied vol from that price
        var price = new BlackScholesPricer().Price(option, market).Price;
        var ivResult = new ImpliedVolatilitySolver().Solve(
            new ImpliedVolatilityRequest(
                option,
                market.WithFlatVolatility(0.15d),
                price.Amount,
                0.15d));

        // Assert — recovered IV should match input within 1e-4
        ivResult.ImpliedVolatility.Should().NotBeNull();
        ivResult.ImpliedVolatility!.Value.Should().BeApproximately(0.20d, 1e-4d);
    }

    [Fact]
    public async Task ExampleMarketHelper_ShouldPassItsRepricingGate_AndPriceAnOption()
    {
        var market = await MarketHelpers.CreateBootstrappedMarketAsync(s_valuationDate, 100m, 0.20d).ConfigureAwait(true);
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));

        var result = new BlackScholesPricer().Price(option, market);

        result.Price.Amount.Should().BeGreaterThan(0m);
        market.DiscountFactor(option.ExpiryDate).Should().BeInRange(0d, 1d);
    }

    [Fact]
    public async Task BootstrappedCurve_ShouldProduceNonFlatDiscountFactors()
    {
        // Arrange — bootstrap and extract discount curve
        var snapshot = await BuildUsdSnapshotAsync().ConfigureAwait(true);
        var curve = (IDiscountCurve)snapshot.CurveGroup.GetCurve(s_usdDiscountRef);

        // Act — sample discount factors at multiple tenors
        var df1Y = curve.DiscountFactor(s_valuationDate.AddYears(1));
        var df5Y = curve.DiscountFactor(s_valuationDate.AddYears(5));
        var df10Y = curve.DiscountFactor(s_valuationDate.AddYears(10));

        // Assert — term structure: shorter maturity should have higher DF
        df1Y.Should().BeGreaterThan(df5Y);
        df5Y.Should().BeGreaterThan(df10Y);

        // Assert — not flat: zero rates at different tenors should differ
        var zr1Y = -Math.Log(df1Y) / 1.0;
        var zr5Y = -Math.Log(df5Y) / 5.0;
        Math.Abs(zr1Y - zr5Y).Should().BeGreaterThan(1e-6);
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
            new RateBenchmark(new BenchmarkName("SOFR"), Currency.USD, BenchmarkKind.OvernightRiskFree, null, 0, 1, "USNY", "ACT/360", true),
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
