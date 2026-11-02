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
/// Tests for Example 23 (Chriss §6.10): CRR trees with vs. without a continuous dividend
/// yield, demonstrating the early-exercise-boundary shift for American calls.
///
/// <para>
/// <b>Dividend modelling note:</b> the CRR tree models dividends as a continuous yield
/// via <see cref="TreeModelInput.DividendCurve"/>. This is the standard Merton
/// constant-yield adjustment (<c>q</c> in the risk-neutral probability
/// <c>p = (exp((r − q) dt) − d) / (u − d)</c>); it is not a discrete-dividend
/// schedule. The examples and tests in this file honestly describe the dividend
/// support as a continuous yield throughout.
/// </para>
///
/// <para>
/// <b>Boundary-shift direction (American call, positive dividend yield):</b>
/// an American call holder may optimally exercise early when the underlying pays
/// dividends, because the dividend yield reduces the forward (the stock drops by the
/// equivalent of a yield on the ex-date), lowering the option's continuation value.
/// Equivalently, the risk-neutral probability decreases when <c>q &gt; 0</c>, making
/// deep in-the-money nodes at late time steps worth exercising immediately.  With zero
/// dividend yield an American call on a non-dividend stock is never worth exercising
/// early (boundary is empty).  With a positive yield the early-exercise boundary
/// activates (boundary is non-empty).  The test asserts this transition.
/// </para>
/// </summary>
public sealed class TreeDividendExampleTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 15);
    private const Currency Usd = Currency.USD;

    /// <summary>
    /// Builds a flat <see cref="OptionMarketData"/> with the given risk-free rate and
    /// optional continuous dividend yield.
    /// </summary>
    private static OptionMarketData CreateMarket(decimal spot, double r, double q)
    {
        var discountCurve = new FlatDiscountCurve(
            new CurveName("USD-OIS"), s_valuationDate, Usd, r);
        var dividendCurve = new FlatDiscountCurve(
            new CurveName("DIV"), s_valuationDate, Usd, q);
        return new OptionMarketData(
            s_valuationDate, Currency.USD.Amount(spot), flatVolatility: 0.20d, discountCurve, dividendCurve);
    }

    private static BinomialTree BuildTree(OptionMarketData market, OptionContract option, int steps = 200)
    {
        var input = TreeModelInput.FromMarketData(market, option.Strike.Amount, option.ExpiryDate, steps);
        return new CoxRossRubinsteinTreeBuilder().Build(input);
    }

    // ── AC-1: Example 23 prices with/without dividend for European and American ──

    /// <summary>
    /// AC-1a: European call price is lower with a positive continuous dividend yield
    /// than without one (dividend yield reduces the forward, lowering call value).
    /// Validates that the pricer uses the dividend curve in the CRR tree.
    /// </summary>
    [Fact]
    public void TreeDividendExample_EuropeanCall_PriceWithDividendLowerThanWithout()
    {
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));

        // Without dividend (q = 0)
        var marketNoDividend = CreateMarket(100m, r: 0.05d, q: 0.0d);
        var treeNoDividend = BuildTree(marketNoDividend, call);
        var priceNoDividend = new EuropeanTreePricer().Price(call, marketNoDividend, treeNoDividend);

        // With 3% continuous dividend yield
        var marketWithDividend = CreateMarket(100m, r: 0.05d, q: 0.03d);
        var treeWithDividend = BuildTree(marketWithDividend, call);
        var priceWithDividend = new EuropeanTreePricer().Price(call, marketWithDividend, treeWithDividend);

        priceNoDividend.Price.Should().BeGreaterThan(priceWithDividend.Price,
            "a positive continuous dividend yield lowers the forward and therefore lowers the European call price");
    }

    /// <summary>
    /// AC-1b: American call price is lower with a positive continuous dividend yield
    /// than without one. The American early-exercise premium softens (but does not
    /// fully offset) the forward reduction.
    /// </summary>
    [Fact]
    public void TreeDividendExample_AmericanCall_PriceWithDividendLowerThanWithout()
    {
        var call = new AmericanOption(CallPut.Call, 100m, Currency.USD, s_valuationDate.AddMonths(6));

        var marketNoDividend = CreateMarket(100m, r: 0.05d, q: 0.0d);
        var treeNoDividend = BuildTree(marketNoDividend, call);
        var priceNoDividend = new AmericanTreePricer().Price(call, marketNoDividend, treeNoDividend);

        var marketWithDividend = CreateMarket(100m, r: 0.05d, q: 0.03d);
        var treeWithDividend = BuildTree(marketWithDividend, call);
        var priceWithDividend = new AmericanTreePricer().Price(call, marketWithDividend, treeWithDividend);

        priceNoDividend.Price.Should().BeGreaterThan(priceWithDividend.Price,
            "positive dividend yield lowers the forward and hence the American call price even with early exercise");
    }

    // ── AC-2: American early-exercise boundary shifts direction when dividend is added ──

    /// <summary>
    /// AC-2 (boundary-shift assertion): For an American call with ZERO dividend yield,
    /// early exercise is never optimal — the boundary must be empty.
    /// With a POSITIVE continuous dividend yield, early exercise can be optimal on
    /// deep in-the-money nodes — the boundary must be non-empty.
    ///
    /// This demonstrates the documented boundary shift: adding a dividend activates
    /// the early-exercise boundary for American calls (boundary goes from empty → non-empty).
    /// The test asserts this direction change against the real
    /// <see cref="AmericanTreePricer.ComputeExerciseBoundary"/> production method.
    /// </summary>
    [Fact]
    public void TreeDividendExample_AmericanCall_BoundaryActivatesWhenDividendAdded()
    {
        // Deep ITM call to ensure some early-exercise nodes exist with dividends.
        var call = new AmericanOption(CallPut.Call, 70m, Currency.USD, s_valuationDate.AddMonths(12));
        var pricer = new AmericanTreePricer();

        // Without dividend: boundary must be empty (no early exercise for non-div call).
        var marketNoDividend = CreateMarket(100m, r: 0.05d, q: 0.0d);
        var treeNoDividend = BuildTree(marketNoDividend, call, steps: 200);
        var boundaryNoDividend = pricer.ComputeExerciseBoundary(call, marketNoDividend, treeNoDividend);

        // With high dividend yield: boundary must be non-empty (early exercise activated).
        var marketWithDividend = CreateMarket(100m, r: 0.05d, q: 0.08d);
        var treeWithDividend = BuildTree(marketWithDividend, call, steps: 200);
        var boundaryWithDividend = pricer.ComputeExerciseBoundary(call, marketWithDividend, treeWithDividend);

        // ASSERT the documented direction:
        // No dividend → boundary is empty (American call never early-exercised without dividends)
        boundaryNoDividend.IsSuccess.Should().BeFalse(
            "an American call on a non-dividend-paying stock is never worth early exercise");
        boundaryNoDividend.Boundaries.Should().BeEmpty(
            "no early-exercise nodes exist when dividend yield is zero");

        // With dividend → boundary is non-empty (early exercise becomes optimal)
        boundaryWithDividend.IsSuccess.Should().BeTrue(
            "a sufficiently high continuous dividend yield makes early exercise optimal for deep ITM American calls");
        boundaryWithDividend.Boundaries.Should().NotBeEmpty(
            "at least some nodes should exercise early when a high dividend yield reduces the continuation value");
    }
}
