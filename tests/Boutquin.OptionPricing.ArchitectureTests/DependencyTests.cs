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

using Boutquin.OptionPricing.ArchitectureTests.Helpers;
using FluentAssertions;
using NetArchTest.Rules;

namespace Boutquin.OptionPricing.ArchitectureTests;

/// <summary>
/// Verifies the dependency graph defined in docs/api-review-v1.md:
///
///   Abstractions → (nothing)
///   Domain       → Abstractions
///   Analytics    → Domain, Abstractions
///   Trees        → Domain, Abstractions
///   Calibration  → Domain, Trees, Analytics, Abstractions
///   Exotics      → Domain, Trees, Calibration, Abstractions  (NOT Analytics)
///
/// Note: Numerics (distributions, interpolation, solvers) now lives in
/// Boutquin.Numerics — an external package, not a sibling project.
/// </summary>
public sealed class DependencyTests : BaseArchitectureTest
{
    private const string Domain = "Boutquin.OptionPricing.Domain";
    private const string Analytics = "Boutquin.OptionPricing.Analytics";
    private const string Trees = "Boutquin.OptionPricing.Trees";
    private const string Calibration = "Boutquin.OptionPricing.Calibration";
    private const string Exotics = "Boutquin.OptionPricing.Exotics";

    // ── Abstractions should not depend on any sibling project ──

    [Theory]
    [InlineData(Domain)]
    [InlineData(Analytics)]
    [InlineData(Trees)]
    [InlineData(Calibration)]
    [InlineData(Exotics)]
    public void Abstractions_ShouldNotDependOn(string forbidden)
    {
        var result = Types.InAssembly(AbstractionsAssembly)
            .Should()
            .NotHaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Abstractions must not depend on {forbidden}: [{GetFailingTypes(result)}]");
    }

    // ── Domain should only depend on Abstractions ──

    [Theory]
    [InlineData(Analytics)]
    [InlineData(Trees)]
    [InlineData(Calibration)]
    [InlineData(Exotics)]
    public void Domain_ShouldNotDependOn(string forbidden)
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Domain must not depend on {forbidden}: [{GetFailingTypes(result)}]");
    }

    // ── Analytics should not depend on Trees, Calibration, Exotics ──

    [Theory]
    [InlineData(Trees)]
    [InlineData(Calibration)]
    [InlineData(Exotics)]
    public void Analytics_ShouldNotDependOn(string forbidden)
    {
        var result = Types.InAssembly(AnalyticsAssembly)
            .Should()
            .NotHaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Analytics must not depend on {forbidden}: [{GetFailingTypes(result)}]");
    }

    // ── Trees should not depend on Analytics, Calibration, Exotics ──

    [Theory]
    [InlineData(Analytics)]
    [InlineData(Calibration)]
    [InlineData(Exotics)]
    public void Trees_ShouldNotDependOn(string forbidden)
    {
        var result = Types.InAssembly(TreesAssembly)
            .Should()
            .NotHaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Trees must not depend on {forbidden}: [{GetFailingTypes(result)}]");
    }

    // ── Calibration should not depend on Exotics ──

    [Fact]
    public void Calibration_ShouldNotDependOnExotics()
    {
        var result = Types.InAssembly(CalibrationAssembly)
            .Should()
            .NotHaveDependencyOnAny(Exotics)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Calibration must not depend on Exotics: [{GetFailingTypes(result)}]");
    }

    // ── Exotics should not depend on Analytics (directly) ──

    [Theory]
    [InlineData(Analytics)]
    public void Exotics_ShouldNotDependOn(string forbidden)
    {
        var result = Types.InAssembly(ExoticsAssembly)
            .Should()
            .NotHaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Exotics must not depend on {forbidden}: [{GetFailingTypes(result)}]");
    }
}
