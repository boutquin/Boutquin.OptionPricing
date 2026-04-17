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
using Boutquin.Curves.Bootstrap;
using Boutquin.Curves.Bootstrap.ReferenceData;
using Boutquin.Curves.Core.Discounting;
using Boutquin.Curves.Recipes;
using Boutquin.Curves.Recipes.Testing;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Calendars.Holidays;
using Boutquin.MarketData.Conventions;
using Boutquin.OptionPricing.Domain.Market;
using Microsoft.Extensions.Logging.Abstractions;

namespace Boutquin.OptionPricing.Examples.Shared;

/// <summary>
/// Shared market construction helpers for all example projects.
/// </summary>
public static class MarketHelpers
{
    /// <summary>
    /// Builds an <see cref="OptionMarketData"/> backed by flat discount and dividend curves.
    /// </summary>
    public static OptionMarketData CreateFlatMarket(
        DateOnly valuationDate,
        decimal spot,
        double flatVolatility,
        double riskFreeRate,
        double dividendYield)
    {
        var discountCurve = new FlatDiscountCurve(new CurveName("USD-OIS"), valuationDate, CurrencyCode.USD, riskFreeRate);
        var dividendCurve = new FlatDiscountCurve(new CurveName("DIVIDEND"), valuationDate, CurrencyCode.USD, dividendYield);
        return new OptionMarketData(valuationDate, spot, flatVolatility, discountCurve, dividendCurve);
    }

    /// <summary>
    /// Bootstraps a USD SOFR discount curve from fixture data and returns the curve snapshot.
    /// </summary>
    public static async Task<CurveSnapshot> BuildUsdSnapshotAsync(DateOnly valuationDate)
    {
        var pipeline = FixtureData.CreatePipeline();
        var calibrator = new PiecewiseBootstrapCalibrator();
        var referenceData = CreateReferenceData();
        var logger = NullLogger<CurveBuilder>.Instance;
        var builder = new CurveBuilder(pipeline, calibrator, referenceData, logger);
        var recipe = StandardCurveRecipes.UsdSofrDiscount();
        return await builder.BuildAsync(recipe, valuationDate).ConfigureAwait(false);
    }

    /// <summary>
    /// Bootstraps a USD SOFR discount curve and returns an <see cref="OptionMarketData"/> sourced from it.
    /// Throws if the bootstrap repricing error exceeds 1e-6.
    /// </summary>
    public static async Task<OptionMarketData> CreateBootstrappedMarketAsync(
        DateOnly valuationDate,
        decimal spot,
        double flatVol)
    {
        var snapshot = await BuildUsdSnapshotAsync(valuationDate).ConfigureAwait(false);
        if (snapshot.Diagnostics.Repricing.Count > 0)
        {
            double maxError = snapshot.Diagnostics.Repricing.Max(r => r.AbsoluteError);
            if (maxError > 1e-6)
                throw new InvalidOperationException($"Curve failed quality gate: max repricing error {maxError:E2}");
        }
        return OptionMarketData.FromCurveGroup(
            snapshot.CurveGroup, CurrencyCode.USD, spot, flatVol, new BenchmarkName("SOFR"));
    }

    /// <summary>
    /// Builds a <see cref="SimpleReferenceDataProvider"/> with USD-SOFR calendars, benchmarks, and conventions.
    /// </summary>
    public static SimpleReferenceDataProvider CreateReferenceData()
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
