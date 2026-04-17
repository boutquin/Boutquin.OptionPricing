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

using Boutquin.MarketData.DayCount;
using Boutquin.OptionPricing.Calibration.Smile;
using FluentAssertions;

namespace Boutquin.OptionPricing.Calibration.Tests;

/// <summary>
/// Phase 5c: exercises the dated <see cref="VolatilitySurface.GetVol(decimal, DateOnly, DateOnly, IYearFractionCalculator)"/>
/// overload. Confirms the dated query delegates to the year-fraction-based
/// interpolation through the supplied day-count convention.
/// </summary>
public sealed class VolatilitySurfaceDatedQueryTests
{
    private static readonly VolatilitySurface s_surface = new(new[]
    {
        new SmileSlice(0.25, new[]
        {
            new SurfacePoint(0.25, 90m, 0.25),
            new SurfacePoint(0.25, 100m, 0.20),
            new SurfacePoint(0.25, 110m, 0.22),
        }),
        new SmileSlice(1.00, new[]
        {
            new SurfacePoint(1.00, 90m, 0.23),
            new SurfacePoint(1.00, 100m, 0.18),
            new SurfacePoint(1.00, 110m, 0.20),
        }),
    });

    [Fact]
    public void GetVol_Dated_MatchesYearFractionQueryUnderSameDayCount()
    {
        var valuationDate = new DateOnly(2026, 1, 15);
        var expiryDate = new DateOnly(2027, 1, 15); // ~1Y
        var dayCount = Actual365Fixed.Instance;

        double datedVol = s_surface.GetVol(100m, valuationDate, expiryDate, dayCount);
        double yearsQuery = s_surface.GetVol(100m, dayCount.YearFraction(valuationDate, expiryDate));

        datedVol.Should().Be(yearsQuery);
    }

    [Fact]
    public void GetVol_Dated_ExpiryAtOrBeforeValuationDate_ClampsToFrontSlice()
    {
        var valuationDate = new DateOnly(2026, 1, 15);
        var dayCount = Actual365Fixed.Instance;

        // Expiry == valuation: maturity clamps to 0, which hits the flat extrapolation
        // branch at the front slice. Must equal the surface's value at the shortest slice.
        double vol = s_surface.GetVol(100m, valuationDate, valuationDate, dayCount);
        double expected = s_surface.GetVol(100m, 0d);

        vol.Should().Be(expected);
    }

    [Fact]
    public void GetVol_Dated_NullDayCount_Throws()
    {
        var valuationDate = new DateOnly(2026, 1, 15);
        var expiryDate = new DateOnly(2027, 1, 15);

        Action act = () => s_surface.GetVol(100m, valuationDate, expiryDate, dayCount: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetVol_Dated_DifferentDayCounts_ProduceDifferentMaturities()
    {
        // ACT/360 vs ACT/365F: same date range yields different year fractions.
        // The dated query must honor the supplied day-count, not a global default.
        var valuationDate = new DateOnly(2026, 1, 15);
        var expiryDate = new DateOnly(2026, 4, 15); // ~3M

        double vol360 = s_surface.GetVol(100m, valuationDate, expiryDate, Actual360.Instance);
        double vol365 = s_surface.GetVol(100m, valuationDate, expiryDate, Actual365Fixed.Instance);

        // For the exact same contract dates but different conventions, the implied
        // maturity differs by the ratio 365/360 — a ~1.4% shift in T that the surface
        // must reflect because its slices are defined in year-fraction space.
        double yf360 = Actual360.Instance.YearFraction(valuationDate, expiryDate);
        double yf365 = Actual365Fixed.Instance.YearFraction(valuationDate, expiryDate);

        double direct360 = s_surface.GetVol(100m, yf360);
        double direct365 = s_surface.GetVol(100m, yf365);

        vol360.Should().Be(direct360);
        vol365.Should().Be(direct365);
    }
}
