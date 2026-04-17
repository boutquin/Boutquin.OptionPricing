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
/// Tests for <see cref="VolatilitySurface.GetVol"/> — strike-axis monotone-cubic
/// (Fritsch-Carlson with arXiv:2402.01324 endpoint refinement) and maturity-axis
/// linear interpolation, with flat extrapolation in both axes.
/// </summary>
/// <remarks>
/// Phase 5b note: under the legacy implementation, the strike axis used two-point
/// linear interpolation. Adoption of <c>MonotoneCubicInterpolator</c> from
/// <c>Boutquin.Numerics</c> changes off-grid strike values for slices with three or
/// more nodes (two-node slices remain linear by construction since the monotone-cubic
/// Hermite reduces algebraically to linear when both endpoint tangents equal the
/// secant slope). On-grid points, flat extrapolation, and the single-slice degenerate
/// case are all unchanged.
/// </remarks>
public sealed class VolatilitySurfaceTests
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
    public void GetVol_ExactPoint_ShouldReturnExactValue()
    {
        s_surface.GetVol(100m, 0.25).Should().BeApproximately(0.20, 1e-10);
        s_surface.GetVol(90m, 1.00).Should().BeApproximately(0.23, 1e-10);
    }

    [Fact]
    public void GetVol_InterpolateStrike_ShouldUseMonotoneCubic()
    {
        // Monotone-cubic Hermite on slice T=0.25 with nodes (90,0.25), (100,0.20), (110,0.22).
        // Finite-difference slopes: δ₀ = -0.005, δ₁ = +0.002 (sign change at interior node).
        // Endpoint tangents (arXiv:2402.01324 three-point one-sided, h₀ = h₁ = 10):
        //   m₀ = (3·δ₀ - δ₁) / 2 = (-0.015 - 0.002)/2 = -0.0085
        // Interior tangent zeroed at the sign change: m₁ = 0.
        // At K=95, s = 0.5, h = 10, the cubic Hermite evaluates to:
        //   y = h₀₀(0.5)·0.25 + h₁₀(0.5)·10·m₀ + h₀₁(0.5)·0.20 + h₁₁(0.5)·10·m₁
        //     = 0.5·0.25 + 0.125·(-0.085) + 0.5·0.20 + (-0.125)·0
        //     = 0.214375
        // (Linear interpolation would give 0.225; the cubic correction reflects the
        // genuine wing curvature implied by the asymmetric smile.)
        s_surface.GetVol(95m, 0.25).Should().BeApproximately(0.214375, 1e-10);
    }

    [Fact]
    public void GetVol_InterpolateMaturity_ShouldInterpolateLinearly()
    {
        // At K=100 between T=0.25 (σ=0.20) and T=1.00 (σ=0.18). Strike is on grid in
        // both bounding slices, so monotone-cubic strike interpolation is exact and
        // only the maturity-axis linear interpolation contributes.
        // At T=0.625 (midpoint): 0.20 + (0.625-0.25)/(1.00-0.25) * (0.18-0.20) = 0.19
        s_surface.GetVol(100m, 0.625).Should().BeApproximately(0.19, 1e-10);
    }

    [Fact]
    public void GetVol_BivariateInterpolation_ShouldComposeMonotoneCubicAndLinear()
    {
        // K=95, T=0.625
        // Strike-axis monotone-cubic on each bounding slice (same node spacing, same
        // delta signs, so endpoint tangent formula yields the same numeric correction):
        //   T=0.25 slice: GetVol(95) = 0.214375 (computed above)
        //   T=1.00 slice with nodes (90,0.23),(100,0.18),(110,0.20):
        //     δ₀ = -0.005, δ₁ = 0.002 — identical structure to T=0.25 slice
        //     m₀ = -0.0085, m₁ = 0 (sign change)
        //     y(95) = 0.5·0.23 + 0.125·(-0.085) + 0.5·0.18 + 0 = 0.194375
        // Maturity-axis linear at T=0.625:
        //   0.214375 + 0.5·(0.194375 - 0.214375) = 0.204375
        s_surface.GetVol(95m, 0.625).Should().BeApproximately(0.204375, 1e-10);
    }

    [Fact]
    public void GetVol_ExtrapolateStrike_ShouldFlatExtend()
    {
        // Below lowest strike (90): use σ at K=90 (MonotoneCubicInterpolator clamps)
        s_surface.GetVol(80m, 0.25).Should().BeApproximately(0.25, 1e-10);
        // Above highest strike (110): use σ at K=110
        s_surface.GetVol(120m, 0.25).Should().BeApproximately(0.22, 1e-10);
    }

    [Fact]
    public void GetVol_ExtrapolateMaturity_ShouldFlatExtend()
    {
        // Below earliest maturity (0.25): use nearest slice
        s_surface.GetVol(100m, 0.10).Should().BeApproximately(0.20, 1e-10);
        // Above latest maturity (1.00): use nearest slice
        s_surface.GetVol(100m, 2.00).Should().BeApproximately(0.18, 1e-10);
    }

    [Fact]
    public void GetVol_SingleSlice_ShouldInterpolateStrikeOnly()
    {
        var singleSlice = new VolatilitySurface(new[]
        {
            new SmileSlice(0.50, new[]
            {
                new SurfacePoint(0.50, 90m, 0.22),
                new SurfacePoint(0.50, 100m, 0.20),
            }),
        });

        // Two-node slice: monotone-cubic Hermite reduces to linear (both endpoint
        // tangents equal the secant slope, so the cubic terms cancel identically).
        // Any maturity falls back to the single slice.
        singleSlice.GetVol(95m, 0.50).Should().BeApproximately(0.21, 1e-10);
        singleSlice.GetVol(95m, 1.00).Should().BeApproximately(0.21, 1e-10);
    }

    [Fact]
    public void GetVol_TwoSlices_ShouldFallBackToLinearMaturityInterp()
    {
        // The cubic-spline maturity scheme requires ≥3 slices for SplineBoundary.Natural
        // and ≥4 for SplineBoundary.ModifiedNotAKnot. With exactly two slices the surface
        // must fall back to two-point linear on the maturity axis. We exercise this with
        // an on-grid strike (K=100) so only the maturity-axis interpolation contributes:
        //   T=0.625 between (0.25, 0.20) and (1.00, 0.18) → 0.20 + 0.5·(-0.02) = 0.19
        // (Identical to GetVol_InterpolateMaturity_ShouldInterpolateLinearly above; this
        // test exists to pin the fallback contract.)
        s_surface.GetVol(100m, 0.625).Should().BeApproximately(0.19, 1e-10);
    }

    [Fact]
    public void GetVol_ThreeSlices_ShouldUseNaturalCubicOnMaturityAxis()
    {
        // Three slices triggers SplineBoundary.Natural on the maturity axis. We use
        // strike = 100 (exact node in every slice) so the strike-axis interpolation is
        // the identity and only the maturity-axis spline contributes.
        var threeSliceSurface = new VolatilitySurface(new[]
        {
            new SmileSlice(0.25, new[] { new SurfacePoint(0.25, 100m, 0.20) }),
            new SmileSlice(1.00, new[] { new SurfacePoint(1.00, 100m, 0.22) }),
            new SmileSlice(2.00, new[] { new SurfacePoint(2.00, 100m, 0.21) }),
        });

        // On-grid maturities round-trip exactly under any spline scheme.
        threeSliceSurface.GetVol(100m, 0.25).Should().BeApproximately(0.20, 1e-10);
        threeSliceSurface.GetVol(100m, 1.00).Should().BeApproximately(0.22, 1e-10);
        threeSliceSurface.GetVol(100m, 2.00).Should().BeApproximately(0.21, 1e-10);

        // Off-grid maturity should differ from a naïve linear interpolation between
        // bounding nodes (this is the whole point of switching off two-point linear).
        // At T=0.5 between (0.25, 0.20) and (1.00, 0.22): linear gives
        //   0.20 + (0.5-0.25)/(1.0-0.25) · (0.22-0.20) = 0.20 + (1/3)·0.02 ≈ 0.20667.
        // The natural cubic spline picks up curvature from the third node and yields a
        // strictly different value.
        double cubicAt05 = threeSliceSurface.GetVol(100m, 0.5);
        const double linearAt05 = 0.20 + (1.0 / 3.0 * 0.02);
        Math.Abs(cubicAt05 - linearAt05).Should().BeGreaterThan(1e-6,
            because: "the cubic spline must reflect the third node's curvature, not local linear");
    }

    [Fact]
    public void GetVol_FourPlusSlices_ShouldUseModifiedNotAKnotOnMaturityAxis()
    {
        // Four slices triggers SplineBoundary.ModifiedNotAKnot (Jarre 2025,
        // arXiv:2507.05083). Strike = 100 isolates the maturity-axis interpolation.
        var fourSliceSurface = new VolatilitySurface(new[]
        {
            new SmileSlice(0.25, new[] { new SurfacePoint(0.25, 100m, 0.20) }),
            new SmileSlice(0.50, new[] { new SurfacePoint(0.50, 100m, 0.21) }),
            new SmileSlice(1.00, new[] { new SurfacePoint(1.00, 100m, 0.22) }),
            new SmileSlice(2.00, new[] { new SurfacePoint(2.00, 100m, 0.21) }),
        });

        // Round-trip on every node.
        fourSliceSurface.GetVol(100m, 0.25).Should().BeApproximately(0.20, 1e-10);
        fourSliceSurface.GetVol(100m, 0.50).Should().BeApproximately(0.21, 1e-10);
        fourSliceSurface.GetVol(100m, 1.00).Should().BeApproximately(0.22, 1e-10);
        fourSliceSurface.GetVol(100m, 2.00).Should().BeApproximately(0.21, 1e-10);

        // Off-grid query must produce a finite, in-band value. With the input range
        // [0.20, 0.22] and a smooth spline, a query between nodes should land within a
        // small ModifiedNotAKnot overshoot tolerance — used here as a sanity bound.
        double v = fourSliceSurface.GetVol(100m, 0.75);
        v.Should().BeInRange(0.18, 0.24,
            because: "a smooth cubic must not overshoot far beyond the data envelope");
    }

    [Fact]
    public void GetVol_MonotoneInputs_ShouldNotOvershoot()
    {
        // Monotone-cubic guarantees no spurious oscillation between data points, so a
        // strictly monotone-decreasing smile must yield strictly monotone-decreasing
        // interpolated vols. This is the property a plain (non-monotone) cubic spline
        // does NOT provide, which is the arbitrage motivation for adopting the
        // Fritsch-Carlson scheme on the strike axis.
        var monotoneSlice = new VolatilitySurface(new[]
        {
            new SmileSlice(0.50, new[]
            {
                new SurfacePoint(0.50, 80m, 0.30),
                new SurfacePoint(0.50, 90m, 0.25),
                new SurfacePoint(0.50, 100m, 0.22),
                new SurfacePoint(0.50, 110m, 0.20),
            }),
        });

        double prev = monotoneSlice.GetVol(80m, 0.50);
        for (decimal k = 81m; k <= 110m; k += 1m)
        {
            double current = monotoneSlice.GetVol(k, 0.50);
            current.Should().BeLessThanOrEqualTo(prev,
                because: "monotone-cubic interpolation must preserve the monotone shape of the input data");
            prev = current;
        }
    }
}
