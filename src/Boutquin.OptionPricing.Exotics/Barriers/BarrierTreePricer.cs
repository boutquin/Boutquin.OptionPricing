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

using Boutquin.Domain.Exceptions;
using Boutquin.Domain.Extensions;
using Boutquin.OptionPricing.Abstractions;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Domain.Results;
using Boutquin.OptionPricing.Trees.Binomial;

namespace Boutquin.OptionPricing.Exotics.Barriers;

/// <summary>
/// Prices barrier options on a pre-built binomial tree using backward induction with
/// barrier monitoring at each node. Knock-out options are priced directly by zeroing
/// node values where the barrier has been breached. Knock-in options are derived via
/// barrier parity: V_in = V_vanilla − V_out.
/// </summary>
/// <remarks>
/// <para>
/// At each node during backward induction, the pricer checks whether the underlying
/// spot has breached the barrier. For knock-out options, breached nodes receive zero
/// continuation value. The barrier breach check uses:
/// </para>
/// <list type="bullet">
/// <item>Up barrier: <c>spot &gt;= barrierLevel</c></item>
/// <item>Down barrier: <c>spot &lt;= barrierLevel</c></item>
/// </list>
/// <para>
/// For knock-in options, the complementary knock-out is computed first, then barrier
/// parity is applied: V_knockIn = V_vanilla − V_knockOut.
/// </para>
/// </remarks>
public sealed class BarrierTreePricer : IContextualOptionPricer<BarrierOption, OptionMarketData, BinomialTree, PriceResult>
{
    /// <summary>
    /// Prices a barrier option on the supplied binomial tree.
    /// </summary>
    /// <param name="option">The barrier option contract specifying strike, barrier level, direction, and knock type.</param>
    /// <param name="market">The market data snapshot providing spot, volatility, and curves.</param>
    /// <param name="tree">The pre-built binomial tree to traverse.</param>
    /// <returns>
    /// A <see cref="PriceResult"/> with the barrier option price. For knock-out options the
    /// price is computed directly; for knock-in options it is derived via barrier parity.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="CurrencyMismatchException">
    /// Thrown when <paramref name="market"/>'s spot currency differs from
    /// <paramref name="option"/>'s contract currency.
    /// </exception>
    public PriceResult Price(BarrierOption option, OptionMarketData market, BinomialTree tree)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(tree);

        if (market.Spot.Currency != option.Currency)
        {
            throw new CurrencyMismatchException(option.Currency, market.Spot.Currency);
        }

        // Check if barrier is already breached at spot (root node)
        double spot0 = tree.SpotLattice[0][0];
        if (BarrierBackwardInduction.IsBarrierBreached(spot0, option))
        {
            if (option.KnockType == BarrierKnockType.Out)
            {
                return new PriceResult(option.Currency.Amount(0m), null, CalculationStatus.Success,
                    new Dictionary<string, object?> { ["barrierBreachedAtSpot"] = true },
                    nameof(BarrierTreePricer));
            }

            // Knock-in already activated at spot → option is vanilla from here
            decimal vanillaPrice = BarrierBackwardInduction.PriceVanilla(option, market, tree);
            return new PriceResult(option.Currency.Amount(vanillaPrice), null, CalculationStatus.Success,
                new Dictionary<string, object?> { ["barrierBreachedAtSpot"] = true, ["knockInActivated"] = true },
                nameof(BarrierTreePricer));
        }

        if (option.KnockType == BarrierKnockType.Out)
        {
            decimal knockOutPrice = BarrierBackwardInduction.PriceKnockOut(option, market, tree);
            return new PriceResult(option.Currency.Amount(knockOutPrice), null, CalculationStatus.Success,
                new Dictionary<string, object?> { ["steps"] = tree.Steps },
                nameof(BarrierTreePricer));
        }

        // Knock-in via parity: V_in = V_vanilla - V_out
        decimal vanilla = BarrierBackwardInduction.PriceVanilla(option, market, tree);
        decimal knockOut = BarrierBackwardInduction.PriceKnockOut(option, market, tree);
        decimal knockIn = vanilla - knockOut;
        return new PriceResult(option.Currency.Amount(Math.Max(knockIn, 0m)), null, CalculationStatus.Success,
            new Dictionary<string, object?>
            {
                ["steps"] = tree.Steps,
                ["vanillaPrice"] = vanilla,
                ["knockOutPrice"] = knockOut
            },
            nameof(BarrierTreePricer));
    }

    /// <summary>
    /// Prices a discretely-monitored barrier option by first applying the
    /// Broadie-Glasserman-Kou continuity correction to the barrier level (via
    /// <see cref="BarrierMonitoringAdjustments.AdjustedBarrierLevel"/>) and then pricing the
    /// shifted barrier on the node-monitored tree. This composes the discrete-monitoring
    /// adjustment with the tree pricer, which otherwise monitors at every node (continuous-like).
    /// </summary>
    /// <param name="option">The barrier option contract whose barrier is continuously specified.</param>
    /// <param name="market">The market data snapshot providing spot, volatility, and curves.</param>
    /// <param name="tree">The pre-built binomial tree to traverse.</param>
    /// <param name="monitoringDates">Number of discrete monitoring dates. Must be at least 1.</param>
    /// <returns>A <see cref="PriceResult"/> for the discretely-monitored barrier option.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="CurrencyMismatchException">
    /// Thrown when <paramref name="market"/>'s spot currency differs from <paramref name="option"/>'s
    /// contract currency.
    /// </exception>
    public PriceResult PriceDiscretelyMonitored(BarrierOption option, OptionMarketData market, BinomialTree tree, int monitoringDates)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(tree);

        decimal adjustedBarrier = BarrierMonitoringAdjustments.AdjustedBarrierLevel(
            option.BarrierLevel,
            option.Direction,
            market.FlatVolatility,
            market.TimeToExpiryInYears(option.ExpiryDate),
            monitoringDates);

        BarrierOption adjusted = option with { BarrierLevel = adjustedBarrier };
        return Price(adjusted, market, tree);
    }
}
