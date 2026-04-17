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
/// Tests for <see cref="SmileSliceBuilder"/> validating maturity filtering and strike ordering.
/// </summary>
public sealed class SmileSliceBuilderTests
{
    [Fact]
    public void Build_ShouldFilterByMaturity()
    {
        var points = new[]
        {
            new SurfacePoint(0.25, 90m, 0.22),
            new SurfacePoint(0.50, 90m, 0.21),  // different maturity
            new SurfacePoint(0.25, 100m, 0.20),
            new SurfacePoint(1.00, 100m, 0.19),  // different maturity
        };

        var slice = new SmileSliceBuilder().Build(0.25, points);

        slice.Points.Should().HaveCount(2);
        slice.Points.Should().OnlyContain(p => Math.Abs(p.MaturityInYears - 0.25) < 1e-9);
    }

    [Fact]
    public void Build_ShouldOrderByStrike()
    {
        var points = new[]
        {
            new SurfacePoint(0.25, 110m, 0.18),
            new SurfacePoint(0.25, 90m, 0.22),
            new SurfacePoint(0.25, 100m, 0.20),
        };

        var slice = new SmileSliceBuilder().Build(0.25, points);

        slice.Points.Select(p => p.Strike).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Build_NoMatchingPoints_ShouldReturnEmptySlice()
    {
        var points = new[]
        {
            new SurfacePoint(0.50, 100m, 0.20),
        };

        var slice = new SmileSliceBuilder().Build(0.25, points);

        slice.Points.Should().BeEmpty();
        slice.MaturityInYears.Should().Be(0.25);
    }
}
