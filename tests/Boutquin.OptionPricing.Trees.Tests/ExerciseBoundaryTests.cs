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
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Trees.Binomial;
using Boutquin.OptionPricing.Trees.Pricing;
using FluentAssertions;

namespace Boutquin.OptionPricing.Trees.Tests;

/// <summary>
/// Tests for <see cref="AmericanTreePricer.ComputeExerciseBoundary"/> (F15): the boundary
/// is populated when early exercise is optimal (an ITM American put with a positive rate)
/// and empty when it is never optimal (an American call on a non-dividend underlier).
/// </summary>
public sealed class ExerciseBoundaryTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 15);
    private const Currency Usd = Currency.USD;

    private static OptionMarketData CreateMarket(decimal spot, double r, double q)
    {
        return new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(spot),
            flatVolatility: 0.20,
            discountCurve: new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, Usd, r),
            dividendCurve: new FlatDiscountCurve(new CurveName("DIV"), s_valuationDate, Usd, q));
    }

    private static BinomialTree BuildTree(OptionMarketData market, OptionContract option, int steps)
    {
        var input = TreeModelInput.FromMarketData(market, option.Strike.Amount, option.ExpiryDate, steps);
        return new CoxRossRubinsteinTreeBuilder().Build(input);
    }

    [Fact]
    public void ComputeExerciseBoundary_ItmAmericanPutWithPositiveRate_IsPopulated()
    {
        var market = CreateMarket(90m, r: 0.10, q: 0.0);
        var put = new AmericanOption(CallPut.Put, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var tree = BuildTree(market, put, 100);

        ExerciseBoundaryResult boundary = new AmericanTreePricer().ComputeExerciseBoundary(put, market, tree);

        boundary.IsSuccess.Should().BeTrue();
        boundary.Boundaries.Should().NotBeEmpty();
        // Every recorded index references a valid node at its step, and steps are ascending.
        boundary.Boundaries.Should().OnlyContain(b => b.LowestExerciseIndex >= 0 && b.LowestExerciseIndex <= b.Step);
        boundary.Boundaries.Select(b => b.Step).Should().BeInAscendingOrder();
    }

    [Fact]
    public void ComputeExerciseBoundary_AmericanCallNoDividend_IsEmpty()
    {
        var market = CreateMarket(100m, r: 0.05, q: 0.0);
        var call = new AmericanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var tree = BuildTree(market, call, 100);

        ExerciseBoundaryResult boundary = new AmericanTreePricer().ComputeExerciseBoundary(call, market, tree);

        boundary.IsSuccess.Should().BeFalse();
        boundary.Boundaries.Should().BeEmpty();
    }

    [Fact]
    public void ComputeExerciseBoundary_NullArguments_Throw()
    {
        var market = CreateMarket(100m, 0.05, 0.0);
        var put = new AmericanOption(CallPut.Put, 100m, Currency.USD, s_valuationDate.AddMonths(6));
        var tree = BuildTree(market, put, 10);
        var pricer = new AmericanTreePricer();

        pricer.Invoking(p => p.ComputeExerciseBoundary(null!, market, tree)).Should().Throw<ArgumentNullException>();
        pricer.Invoking(p => p.ComputeExerciseBoundary(put, null!, tree)).Should().Throw<ArgumentNullException>();
        pricer.Invoking(p => p.ComputeExerciseBoundary(put, market, null!)).Should().Throw<ArgumentNullException>();
    }
}
