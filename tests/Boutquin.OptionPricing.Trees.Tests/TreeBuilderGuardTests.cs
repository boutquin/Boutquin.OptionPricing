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
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Trees.Binomial;
using FluentAssertions;

namespace Boutquin.OptionPricing.Trees.Tests;

/// <summary>
/// Argument-validation guards for the tree builders: null input rejection
/// (F-L23-03) and the upper bound on <see cref="TreeModelInput.Steps"/> that
/// caps the O(N^2) lattice allocation (F-L5-01 / F-L5-02).
/// </summary>
public sealed class TreeBuilderGuardTests
{
    public static IEnumerable<object[]> Builders()
    {
        yield return new object[] { new CoxRossRubinsteinTreeBuilder() };
        yield return new object[] { new JarrowRuddTreeBuilder() };
    }

    [Theory]
    [MemberData(nameof(Builders))]
    public void Build_NullInput_ThrowsArgumentNullException(ITreeBuilder builder)
    {
        Action act = () => builder.Build(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [MemberData(nameof(Builders))]
    public void Build_StepsAboveCeiling_ThrowsArgumentOutOfRangeException(ITreeBuilder builder)
    {
        // A five-figure step count produces a multi-GB triangular lattice (Steps^2/2 doubles);
        // an unbounded Build would OOM the process. The builders must reject any Steps above the
        // documented ceiling with a controlled ArgumentOutOfRangeException (paramName "input")
        // BEFORE the Steps+1 capacity arithmetic that overflows at int.MaxValue.
        var input = CreateInput(steps: TreeBuilderLimits.MaxSteps + 1);

        Action act = () => builder.Build(input);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("input");
    }

    [Theory]
    [MemberData(nameof(Builders))]
    public void Build_StepsAtCeiling_DoesNotThrowOutOfRange(ITreeBuilder builder)
    {
        // The ceiling itself must remain a legal step count (boundary inclusivity), so the
        // guard rejects only Steps strictly above MaxSteps.
        var input = CreateInput(steps: TreeBuilderLimits.MaxSteps);

        Action act = () => builder.Build(input);

        act.Should().NotThrow<ArgumentOutOfRangeException>();
    }

    private static TreeModelInput CreateInput(int steps)
    {
        DateOnly valuationDate = new DateOnly(2026, 6, 15);
        var market = new OptionMarketData(
            valuationDate,
            Currency.USD.Amount(100m),
            0.20d,
            new FlatDiscountCurve(new CurveName("USD-OIS"), valuationDate, Currency.USD, 0.03d),
            new FlatDiscountCurve(new CurveName("DIVIDEND"), valuationDate, Currency.USD, 0.01d));
        return TreeModelInput.FromMarketData(market, 100m, valuationDate.AddMonths(6), steps);
    }
}
