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

using Boutquin.OptionPricing.Abstractions;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Trees.Binomial;

namespace Boutquin.OptionPricing.Calibration.ImpliedTrees;

/// <summary>
/// Calibrator for the Derman-Kani implied volatility tree model. Constructs a
/// recombining implied tree whose local volatilities match the market-observed
/// volatility surface, enabling consistent exotic option pricing under the smile.
/// </summary>
/// <remarks>
/// <para>
/// <b>Algorithm</b> (Derman &amp; Kani 1994, "The Volatility Smile and Its Implied Tree"),
/// implemented by <see cref="ImpliedTreeForwardInduction"/>:
/// </para>
/// <list type="number">
/// <item>Anchor each level on the centering condition (central node at spot S₀).</item>
/// <item>From the center outward, solve each node spot from the market call (upper wing)
/// or put (lower wing) struck at the prior level's node, read from the surface.</item>
/// <item>Set transition probabilities from the forward condition
/// <c>F_i = p·S_up + (1−p)·S_down</c> and forward-induct the Arrow-Debreu prices.</item>
/// <item>Extract local volatilities from node spacing: σ_local = ln(S_up/S_down) / (2√dt).</item>
/// <item>Revert any node that would violate no-arbitrage to the constant-volatility spacing.</item>
/// </list>
/// <para>
/// The construction genuinely consumes the smile and reprices the near-the-money region
/// to finite-tree accuracy; like every finite implied tree it is unstable in the deep wings.
/// </para>
/// </remarks>
public sealed class ImpliedVolatilityTreeCalibrator : ICalibrator<ImpliedVolatilityTreeCalibrationInput, ImpliedTreeResult>
{
    private const double Epsilon = 1e-6;

    /// <summary>
    /// Calibrates a Derman-Kani-style implied volatility tree to the volatility surface.
    /// </summary>
    /// <param name="input">Calibration input containing the target volatility surface, market data, and the desired tree step count.</param>
    /// <returns>An <see cref="ImpliedTreeResult"/> with the calibrated tree, local vols, and AD prices.</returns>
    /// <exception cref="ArgumentException">The input, step count, horizon, or representability of tree factors is invalid.</exception>
    /// <exception cref="InvalidOperationException">A quote provider returns an invalid premium, or induction cannot form admissible finite transitions.</exception>
    public ImpliedTreeResult Calibrate(ImpliedVolatilityTreeCalibrationInput input)
    {
        ImpliedTreeCalibration.ValidateInput(input);

        int n = input.Steps;
        OptionMarketData market = input.Market;
        DateOnly expiryDate = ComputeExpiryDate(market, input);
        double t = market.TimeToExpiryInYears(expiryDate);
        if (t <= 0d || !double.IsFinite(t))
        {
            throw new ArgumentException("Calibration requires a maturity that resolves to a future date.", nameof(input));
        }
        double r = market.RiskFreeRate(expiryDate);
        // Effective yield folds the borrow cost into the dividend yield (effectiveQ = q + b),
        // matching the library carry convention (BlackScholesPricer, the tree builders, and
        // OptionMarketData.ForwardPrice = S·exp((r − q − b)·T)). The forward-induction growth term
        // is exp((r − effectiveQ)·dt) = exp((r − q − b)·dt), so the calibrated tree's risk-neutral
        // forward matches the library's no-arbitrage forward.
        double q = market.DividendYield(expiryDate) + market.BorrowRate(expiryDate);
        double s0 = (double)market.Spot.Amount;
        double sigma0 = market.FlatVolatility;

        IOptionPriceProvider priceProvider = input.PriceProvider
            ?? new SurfaceDerivedPriceProvider(input.Surface, market, expiryDate);

        ImpliedTreeForwardInduction.Lattices built = ImpliedTreeForwardInduction.Build(
            s0, sigma0, r, q, t, n,
            (k, maturity) => priceProvider.GetCallPrice((decimal)k, maturity),
            (k, maturity) => priceProvider.GetPutPrice((decimal)k, maturity),
            Epsilon);

        var tree = new BinomialTree(
            n,
            t / n,
            built.UpFactor,
            built.DownFactor,
            built.AverageProbability,
            built.SpotLattice)
        {
            // Carry the per-node martingale probabilities so backward-induction pricers roll the
            // calibrated smile tree back with each node's own weight, not the surface average.
            RiskFreeRate = r,
            ProbabilityLattice = built.ProbabilityLattice
        };

        var report = new CalibrationReport(
            nameof(ImpliedVolatilityTreeCalibrator),
            ImpliedTreeCalibration.StatusFromReversions(built.ReversionCount),
            new Dictionary<string, object?>
            {
                ["steps"] = n,
                ["surfaceSlices"] = input.Surface.Slices.Count,
                ["baseVolatility"] = sigma0,
                ["construction"] = "derman-kani-forward-induction",
                ["nodeReversions"] = built.ReversionCount
            });

        return new ImpliedTreeResult(tree, built.LocalVolatilities, built.ArrowDebreuPrices, report);
    }

    private static DateOnly ComputeExpiryDate(OptionMarketData market, ImpliedVolatilityTreeCalibrationInput input)
    {
        // Phase 5c: invert the year fraction through the discount curve's day-count
        // convention instead of the ad-hoc 365.25 divisor — keeps time-axis semantics
        // consistent with every other T used by the pricing stack.
        double maxMaturity = input.Surface.Slices.Max(s => s.MaturityInYears);
        return market.InvertYearFraction(maxMaturity);
    }
}
