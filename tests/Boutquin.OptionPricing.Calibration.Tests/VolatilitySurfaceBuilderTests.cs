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
/// Tests for <see cref="VolatilitySurfaceBuilder"/> validating maturity grouping and ordering.
/// </summary>
public sealed class VolatilitySurfaceBuilderTests
{
    [Fact]
    public void Build_ShouldGroupByMaturityAscending()
    {
        var points = new[]
        {
            new SurfacePoint(1.00, 100m, 0.19),
            new SurfacePoint(0.25, 90m, 0.22),
            new SurfacePoint(0.25, 100m, 0.20),
            new SurfacePoint(1.00, 90m, 0.21),
            new SurfacePoint(0.50, 100m, 0.20),
        };

        var surface = new VolatilitySurfaceBuilder().Build(points);

        surface.Slices.Should().HaveCount(3);
        surface.Slices.Select(s => s.MaturityInYears).Should().BeInAscendingOrder();
        surface.Slices[0].Points.Select(p => p.Strike).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Build_EmptyInput_ShouldReturnEmptySurface()
    {
        var surface = new VolatilitySurfaceBuilder().Build(Array.Empty<SurfacePoint>());

        surface.Slices.Should().BeEmpty();
    }

    [Fact]
    public void Build_SingleMaturity_ShouldProduceSingleSlice()
    {
        var points = new[]
        {
            new SurfacePoint(0.25, 110m, 0.18),
            new SurfacePoint(0.25, 90m, 0.22),
            new SurfacePoint(0.25, 100m, 0.20),
        };

        var surface = new VolatilitySurfaceBuilder().Build(points);

        surface.Slices.Should().HaveCount(1);
        surface.Slices[0].Points.Should().HaveCount(3);
    }
}
