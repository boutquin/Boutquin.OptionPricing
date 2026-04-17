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

using Boutquin.Numerics.Interpolation;
using FluentAssertions;

namespace Boutquin.OptionPricing.Numerics.Tests;

/// <summary>
/// Tests for the natural cubic spline interpolator.
/// </summary>
public sealed class CubicSplineInterpolatorTests
{
    [Fact]
    public void Interpolate_ShouldReproduceCubicPolynomialAccurately()
    {
        // f(x) = x^3 - 2x^2 + 3x - 1
        // With many knots, natural boundary effects diminish and interior accuracy is high.
        double[] xs = Enumerable.Range(0, 21).Select(i => i * 0.2d).ToArray();
        double[] ys = xs.Select(x => (x * x * x) - (2d * x * x) + (3d * x) - 1d).ToArray();

        var spline = new CubicSplineInterpolator(xs, ys);

        // Test at interior midpoints (away from boundaries)
        double[] testPoints = [1.3d, 1.7d, 2.1d, 2.5d, 2.9d];
        foreach (double x in testPoints)
        {
            double expected = (x * x * x) - (2d * x * x) + (3d * x) - 1d;
            spline.Interpolate(x).Should().BeApproximately(expected, 1e-4d,
                $"spline should accurately reproduce cubic polynomial at x={x}");
        }
    }

    [Fact]
    public void Interpolate_ShouldPassThroughAllDataPoints()
    {
        double[] xs = [1d, 2d, 3d, 4d, 5d];
        double[] ys = [2.1d, 3.7d, 1.2d, 4.5d, 2.8d];

        var spline = new CubicSplineInterpolator(xs, ys);

        for (int i = 0; i < xs.Length; i++)
        {
            spline.Interpolate(xs[i]).Should().BeApproximately(ys[i], 1e-12d,
                $"spline must pass through data point {i}");
        }
    }

    [Fact]
    public void Interpolate_ThreePointMinimum_ShouldWork()
    {
        double[] xs = [0d, 1d, 2d];
        double[] ys = [0d, 1d, 0d];

        var spline = new CubicSplineInterpolator(xs, ys);

        spline.Interpolate(0.5d).Should().BeGreaterThan(0d);
        spline.Interpolate(1.5d).Should().BeGreaterThan(0d);
    }

    [Fact]
    public void Constructor_TwoPoints_ShouldThrow()
    {
        double[] xs = [0d, 1d];
        double[] ys = [0d, 1d];

        var act = () => new CubicSplineInterpolator(xs, ys);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NonIncreasingX_ShouldThrow()
    {
        double[] xs = [0d, 2d, 1d];
        double[] ys = [0d, 1d, 2d];

        var act = () => new CubicSplineInterpolator(xs, ys);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Interpolate_Extrapolation_ShouldUseNearestSegment()
    {
        double[] xs = [0d, 1d, 2d, 3d];
        double[] ys = [0d, 1d, 4d, 9d]; // roughly x^2

        var spline = new CubicSplineInterpolator(xs, ys);

        // Should not throw and should return a finite value
        double left = spline.Interpolate(-1d);
        double right = spline.Interpolate(5d);
        double.IsFinite(left).Should().BeTrue();
        double.IsFinite(right).Should().BeTrue();
    }
}
