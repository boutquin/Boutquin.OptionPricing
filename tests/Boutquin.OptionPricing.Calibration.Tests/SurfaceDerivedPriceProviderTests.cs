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
using Boutquin.OptionPricing.Calibration.ImpliedTrees;
using Boutquin.OptionPricing.Calibration.Smile;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Calibration.Tests;

/// <summary>
/// Tests for <see cref="SurfaceDerivedPriceProvider"/> — verifying that surface-derived
/// prices match direct Black-Scholes pricing.
/// </summary>
public sealed class SurfaceDerivedPriceProviderTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 1);
    private static readonly DateOnly s_expiryDate = new(2026, 7, 1);

    [Fact]
    public void GetCallPrice_FlatSurface_ShouldMatchBlackScholes()
    {
        // Flat surface at 20% vol should produce same price as BS with 20% vol
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20, 100m);
        var provider = new SurfaceDerivedPriceProvider(surface, market, s_expiryDate);

        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_expiryDate);
        double bsPrice = (double)new BlackScholesPricer().Price(option, market).Price.Amount;
        double t = market.TimeToExpiryInYears(s_expiryDate);

        double providerPrice = provider.GetCallPrice(100m, t);

        providerPrice.Should().BeApproximately(bsPrice, 1e-6);
    }

    [Fact]
    public void GetPutPrice_FlatSurface_ShouldMatchBlackScholes()
    {
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateFlatSurface(0.20, 100m);
        var provider = new SurfaceDerivedPriceProvider(surface, market, s_expiryDate);

        var option = new EuropeanOption(CallPut.Put, 100m, Currency.USD, s_expiryDate);
        double bsPrice = (double)new BlackScholesPricer().Price(option, market).Price.Amount;
        double t = market.TimeToExpiryInYears(s_expiryDate);

        double providerPrice = provider.GetPutPrice(100m, t);

        providerPrice.Should().BeApproximately(bsPrice, 1e-6);
    }

    [Fact]
    public void GetCallPrice_SmiledSurface_ShouldDifferFromFlatVol()
    {
        // With a smile, OTM call price should differ from flat-vol price
        var market = CreateMarket(100m, 0.20, 0.05);
        var surface = CreateSmiledSurface();
        var provider = new SurfaceDerivedPriceProvider(surface, market, s_expiryDate);

        double t = market.TimeToExpiryInYears(s_expiryDate);
        double smiledPrice = provider.GetCallPrice(110m, t);

        // Get flat-vol price for comparison
        var flatSurface = CreateFlatSurface(0.20, 100m);
        var flatProvider = new SurfaceDerivedPriceProvider(flatSurface, market, s_expiryDate);
        double flatPrice = flatProvider.GetCallPrice(110m, t);

        // Prices should differ because surface has non-flat smile
        Math.Abs(smiledPrice - flatPrice).Should().BeGreaterThan(0.01);
    }

    private static OptionMarketData CreateMarket(decimal spot, double vol, double rate)
    {
        return new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(spot),
            vol,
            new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, Currency.USD, rate));
    }

    private static VolatilitySurface CreateFlatSurface(double vol, decimal atm)
    {
        var points = new List<SurfacePoint>
        {
            new(0.5, atm - 20m, vol),
            new(0.5, atm, vol),
            new(0.5, atm + 20m, vol)
        };
        return new VolatilitySurface(new[] { new SmileSlice(0.5, points) });
    }

    private static VolatilitySurface CreateSmiledSurface()
    {
        // Smile: higher vol at wings, lower ATM
        var points = new List<SurfacePoint>
        {
            new(0.5, 80m, 0.30),
            new(0.5, 90m, 0.24),
            new(0.5, 100m, 0.20),
            new(0.5, 110m, 0.22),
            new(0.5, 120m, 0.28)
        };
        return new VolatilitySurface(new[] { new SmileSlice(0.5, points) });
    }
}
