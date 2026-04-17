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
/// Verifies naming conventions across all assemblies per v1 spec §12.
/// </summary>
public sealed class NamingConventionTests : BaseArchitectureTest
{
    private static IEnumerable<Assembly> AllAssemblies =>
    [
        AbstractionsAssembly, DomainAssembly,
        AnalyticsAssembly, TreesAssembly, CalibrationAssembly, ExoticsAssembly
    ];

    [Fact]
    public void Interfaces_ShouldStartWithI()
    {
        foreach (var assembly in AllAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .AreInterfaces()
                .Should()
                .HaveNameStartingWith("I")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"interfaces in {assembly.GetName().Name} must start with 'I': [{GetFailingTypes(result)}]");
        }
    }

    [Fact]
    public void Pricers_ShouldEndWithPricer()
    {
        foreach (var assembly in AllAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .ImplementInterface(typeof(Abstractions.IOptionPricer<,,>))
                .Or()
                .ImplementInterface(typeof(Abstractions.IContextualOptionPricer<,,,>))
                .Should()
                .HaveNameEndingWith("Pricer")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"pricer implementations in {assembly.GetName().Name} must end with 'Pricer': [{GetFailingTypes(result)}]");
        }
    }

    [Fact]
    public void TreeBuilders_ShouldEndWithBuilder()
    {
        var result = Types.InAssembly(TreesAssembly)
            .That()
            .ImplementInterface(typeof(Trees.Binomial.ITreeBuilder))
            .Should()
            .HaveNameEndingWith("Builder")
            .Or()
            .HaveNameEndingWith("TreeBuilder")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"ITreeBuilder implementations must end with 'Builder': [{GetFailingTypes(result)}]");
    }

    [Fact]
    public void Calibrators_ShouldEndWithCalibrator()
    {
        foreach (var assembly in AllAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .That()
                .ImplementInterface(typeof(Abstractions.ICalibrator<,>))
                .Should()
                .HaveNameEndingWith("Calibrator")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"ICalibrator implementations in {assembly.GetName().Name} must end with 'Calibrator': [{GetFailingTypes(result)}]");
        }
    }

}
