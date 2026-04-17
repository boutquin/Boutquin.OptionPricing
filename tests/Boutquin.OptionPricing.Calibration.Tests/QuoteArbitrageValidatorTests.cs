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

using Boutquin.OptionPricing.Calibration.Smile;
using FluentAssertions;

namespace Boutquin.OptionPricing.Calibration.Tests;

/// <summary>
/// Tests for <see cref="QuoteArbitrageValidator"/> validating positivity, butterfly, and calendar checks.
/// </summary>
public sealed class QuoteArbitrageValidatorTests
{
    [Fact]
    public void Validate_AllPositiveIV_ShouldBeValid()
    {
        var slice = new SmileSlice(0.25, new[]
        {
            new SurfacePoint(0.25, 90m, 0.22),
            new SurfacePoint(0.25, 100m, 0.20),
            new SurfacePoint(0.25, 110m, 0.21),
        });

        var result = new QuoteArbitrageValidator().Validate(slice);

        result.IsValid.Should().BeTrue();
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ZeroIV_ShouldDetectViolation()
    {
        var slice = new SmileSlice(0.25, new[]
        {
            new SurfacePoint(0.25, 90m, 0.22),
            new SurfacePoint(0.25, 100m, 0.0),
            new SurfacePoint(0.25, 110m, 0.21),
        });

        var result = new QuoteArbitrageValidator().Validate(slice);

        result.IsValid.Should().BeFalse();
        result.Findings.Should().ContainSingle().Which.Should().Contain("100");
    }

    [Fact]
    public void Validate_NegativeIV_ShouldDetectViolation()
    {
        var slice = new SmileSlice(0.25, new[]
        {
            new SurfacePoint(0.25, 90m, -0.10),
            new SurfacePoint(0.25, 100m, 0.20),
        });

        var result = new QuoteArbitrageValidator().Validate(slice);

        result.IsValid.Should().BeFalse();
        result.Findings.Should().ContainSingle().Which.Should().Contain("90");
    }

    [Fact]
    public void Validate_ButterflySpreadViolation_ShouldDetect()
    {
        // Non-convex smile: middle strike vol exceeds linear interpolation of neighbors
        // K1=90 σ=0.20, K2=100 σ=0.30, K3=110 σ=0.20
        // Linear interp at K2: 0.20 + (100-90)/(110-90) * (0.20-0.20) = 0.20
        // Actual σ(K2) = 0.30 > 0.20 → butterfly violation
        var slice = new SmileSlice(0.25, new[]
        {
            new SurfacePoint(0.25, 90m, 0.20),
            new SurfacePoint(0.25, 100m, 0.30),
            new SurfacePoint(0.25, 110m, 0.20),
        });

        var result = new QuoteArbitrageValidator().Validate(slice);

        result.IsValid.Should().BeFalse();
        result.Findings.Should().Contain(f => f.Contains("Butterfly") || f.Contains("butterfly"));
    }

    [Fact]
    public void Validate_ConvexSmile_ShouldPassButterflyCheck()
    {
        // Convex smile (U-shape): middle vol is below linear interpolation
        // K1=90 σ=0.25, K2=100 σ=0.20, K3=110 σ=0.25
        // Linear interp at K2: 0.25 → actual 0.20 < 0.25 → no violation
        var slice = new SmileSlice(0.25, new[]
        {
            new SurfacePoint(0.25, 90m, 0.25),
            new SurfacePoint(0.25, 100m, 0.20),
            new SurfacePoint(0.25, 110m, 0.25),
        });

        var result = new QuoteArbitrageValidator().Validate(slice);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateSurface_CalendarSpreadViolation_ShouldDetect()
    {
        // Total variance must increase with maturity: σ²*T
        // T1=0.25, σ=0.30 → totalVar = 0.09 * 0.25 = 0.0225
        // T2=1.00, σ=0.10 → totalVar = 0.01 * 1.00 = 0.0100 < 0.0225 → violation
        var surface = new VolatilitySurface(new[]
        {
            new SmileSlice(0.25, new[] { new SurfacePoint(0.25, 100m, 0.30) }),
            new SmileSlice(1.00, new[] { new SurfacePoint(1.00, 100m, 0.10) }),
        });

        var result = new QuoteArbitrageValidator().Validate(surface);

        result.IsValid.Should().BeFalse();
        result.Findings.Should().Contain(f => f.Contains("Calendar") || f.Contains("calendar"));
    }

    [Fact]
    public void ValidateSurface_ValidTermStructure_ShouldPass()
    {
        // Total variance increasing: σ²*T
        // T1=0.25, σ=0.20 → totalVar = 0.04 * 0.25 = 0.01
        // T2=1.00, σ=0.20 → totalVar = 0.04 * 1.00 = 0.04 > 0.01 → OK
        var surface = new VolatilitySurface(new[]
        {
            new SmileSlice(0.25, new[] { new SurfacePoint(0.25, 100m, 0.20) }),
            new SmileSlice(1.00, new[] { new SurfacePoint(1.00, 100m, 0.20) }),
        });

        var result = new QuoteArbitrageValidator().Validate(surface);

        result.IsValid.Should().BeTrue();
    }
}
