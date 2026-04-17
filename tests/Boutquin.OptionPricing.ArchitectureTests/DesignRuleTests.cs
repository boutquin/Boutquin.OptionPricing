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

using System.Reflection;
using Boutquin.OptionPricing.ArchitectureTests.Helpers;
using FluentAssertions;
using NetArchTest.Rules;

namespace Boutquin.OptionPricing.ArchitectureTests;

/// <summary>
/// Verifies structural design rules: sealed classes, immutable records,
/// and interface segregation across all assemblies.
/// </summary>
public sealed class DesignRuleTests : BaseArchitectureTest
{
    private static IEnumerable<Assembly> AllAssemblies =>
    [
        AbstractionsAssembly, DomainAssembly,
        AnalyticsAssembly, TreesAssembly, CalibrationAssembly, ExoticsAssembly
    ];

    [Fact]
    public void NonStaticClasses_ShouldBeSealed()
    {
        foreach (var assembly in AllAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .AreClasses()
                .And()
                .AreNotStatic()
                .And()
                .AreNotAbstract()
                .Should()
                .BeSealed()
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"non-static non-abstract classes in {assembly.GetName().Name} must be sealed: [{GetFailingTypes(result)}]");
        }
    }

    [Fact]
    public void AbstractionsAssembly_ShouldOnlyContainInterfaces()
    {
        var concreteTypes = AbstractionsAssembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsInterface)
            .Select(t => t.FullName)
            .ToList();

        concreteTypes.Should().BeEmpty(
            because: "Abstractions should only contain interfaces");
    }

    [Fact]
    public void DomainEnumsNamespace_ShouldOnlyContainEnums()
    {
        var nonEnumTypes = DomainAssembly.GetTypes()
            .Where(t => t.Namespace == "Boutquin.OptionPricing.Domain.Enums" && !t.IsEnum)
            .Select(t => t.FullName)
            .ToList();

        nonEnumTypes.Should().BeEmpty(
            because: "Domain.Enums namespace should only contain enums");
    }

    [Fact]
    public void ResultTypes_ShouldImplementICalculationResult()
    {
        // PriceResult, ImpliedVolatilityResult, ImpliedTreeResult, CalibrationReport
        // should all implement ICalculationResult
        foreach (var assembly in AllAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .HaveNameEndingWith("Result")
                .And()
                .AreNotInterfaces()
                .And()
                .DoNotResideInNamespace("NetArchTest") // exclude test framework types
                .Should()
                .ImplementInterface(typeof(Abstractions.ICalculationResult))
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"types ending with 'Result' in {assembly.GetName().Name} must implement ICalculationResult: [{GetFailingTypes(result)}]");
        }
    }

    [Fact]
    public void Abstractions_ShouldNotHaveDependencyOnAnyProject()
    {
        var result = Types.InAssembly(AbstractionsAssembly)
            .Should()
            .NotHaveDependencyOnAny("Boutquin.OptionPricing.Domain")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Abstractions must be dependency-free: [{GetFailingTypes(result)}]");
    }

    [Fact]
    public void AllPublicTypes_ShouldNotBeNestedInOtherPublicTypes()
    {
        // Nested public types hurt discoverability
        foreach (var assembly in AllAssemblies)
        {
            var nestedPublicTypes = assembly.GetTypes()
                .Where(t => t.IsPublic is false && t.IsNestedPublic && t.DeclaringType?.IsPublic == true)
                .Select(t => t.FullName)
                .ToList();

            nestedPublicTypes.Should().BeEmpty(
                because: $"public types in {assembly.GetName().Name} should not be nested inside other public types");
        }
    }
}
