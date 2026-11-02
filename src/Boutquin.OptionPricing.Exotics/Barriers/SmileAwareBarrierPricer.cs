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
using Boutquin.OptionPricing.Calibration.ImpliedTrees;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Domain.Results;
using Boutquin.OptionPricing.Trees.Binomial;

namespace Boutquin.OptionPricing.Exotics.Barriers;

/// <summary>
/// Prices barrier options using a calibrated implied tree that accounts for the
/// volatility smile. The calibrated tree's spot lattice already reflects smile-consistent
/// local volatilities, correcting for smile effects that flat-volatility barrier
/// tree pricing ignores.
/// </summary>
/// <remarks>
/// <para>
/// Uses the same backward induction with barrier monitoring as <see cref="BarrierTreePricer"/>,
/// but operates on the calibrated implied tree from <see cref="ImpliedTreeResult"/> rather
/// than a flat-vol CRR tree. For knock-in options, barrier parity is applied:
/// V_in = V_vanilla − V_out.
/// </para>
/// </remarks>
public sealed class SmileAwareBarrierPricer : IContextualOptionPricer<BarrierOption, OptionMarketData, ImpliedTreeResult, PriceResult>
{
    /// <summary>
    /// Prices a barrier option using a calibrated smile-aware implied tree.
    /// </summary>
    /// <param name="option">The barrier option contract.</param>
    /// <param name="market">The market data snapshot.</param>
    /// <param name="calibration">
    /// The calibrated implied tree result containing the smile-consistent
    /// spot lattice, local volatilities, and Arrow-Debreu state prices.
    /// </param>
    /// <returns>
    /// A <see cref="PriceResult"/> with the smile-aware barrier option price.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="CurrencyMismatchException">
    /// Thrown when <paramref name="market"/>'s spot currency differs from
    /// <paramref name="option"/>'s contract currency.
    /// </exception>
    public PriceResult Price(BarrierOption option, OptionMarketData market, ImpliedTreeResult calibration)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(calibration);

        if (market.Spot.Currency != option.Currency)
        {
            throw new CurrencyMismatchException(option.Currency, market.Spot.Currency);
        }

        if (!calibration.IsSuccess)
        {
            return PriceResult.Unsupported(
                nameof(SmileAwareBarrierPricer),
                "Cannot price: calibration did not succeed.",
                option.Currency);
        }

        BinomialTree tree = calibration.Tree;

        // Check if barrier is already breached at spot
        double spot0 = tree.SpotLattice[0][0];
        if (BarrierBackwardInduction.IsBarrierBreached(spot0, option))
        {
            if (option.KnockType == BarrierKnockType.Out)
            {
                return new PriceResult(option.Currency.Amount(0m), null, CalculationStatus.Success,
                    new Dictionary<string, object?> { ["barrierBreachedAtSpot"] = true },
                    nameof(SmileAwareBarrierPricer));
            }

            decimal vanillaPrice = BarrierBackwardInduction.PriceVanilla(option, market, tree);
            return new PriceResult(option.Currency.Amount(vanillaPrice), null, CalculationStatus.Success,
                new Dictionary<string, object?> { ["barrierBreachedAtSpot"] = true, ["knockInActivated"] = true },
                nameof(SmileAwareBarrierPricer));
        }

        if (option.KnockType == BarrierKnockType.Out)
        {
            decimal knockOutPrice = BarrierBackwardInduction.PriceKnockOut(option, market, tree);
            return new PriceResult(option.Currency.Amount(knockOutPrice), null, CalculationStatus.Success,
                new Dictionary<string, object?> { ["steps"] = tree.Steps, ["model"] = "smile-aware" },
                nameof(SmileAwareBarrierPricer));
        }

        // Knock-in via parity
        decimal vanilla = BarrierBackwardInduction.PriceVanilla(option, market, tree);
        decimal knockOut = BarrierBackwardInduction.PriceKnockOut(option, market, tree);
        decimal knockIn = Math.Max(vanilla - knockOut, 0m);
        return new PriceResult(option.Currency.Amount(knockIn), null, CalculationStatus.Success,
            new Dictionary<string, object?>
            {
                ["steps"] = tree.Steps,
                ["model"] = "smile-aware",
                ["vanillaPrice"] = vanilla,
                ["knockOutPrice"] = knockOut
            },
            nameof(SmileAwareBarrierPricer));
    }
}
