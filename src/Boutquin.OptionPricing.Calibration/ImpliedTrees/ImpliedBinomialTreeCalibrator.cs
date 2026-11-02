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
/// Calibrator for the implied binomial tree. Recovers a recombining binomial tree whose
/// Arrow-Debreu state prices reproduce the observed volatility surface, yielding local
/// volatility estimates at every node.
/// </summary>
/// <remarks>
/// <para>
/// The tree is built by the Derman-Kani forward induction (see
/// <see cref="ImpliedTreeForwardInduction"/>) — the canonical, numerically robust
/// construction of a smile-consistent implied binomial tree. Rubinstein's (1994)
/// terminal-distribution method yields an equivalent smile-consistent tree; this
/// implementation uses the forward-induction construction because it consumes the full
/// surface (every maturity and strike) rather than only the terminal distribution.
/// </para>
/// <para>
/// <b>Accuracy.</b> The construction reprices the near-the-money calibration region to
/// finite-tree accuracy and produces a genuinely smile-shaped lattice (a non-flat surface
/// yields a non-uniform spot lattice and node-varying local volatilities). Like every
/// finite implied tree it is unstable in the deep wings, where nodes revert to the
/// constant-volatility spacing.
/// </para>
/// </remarks>
public sealed class ImpliedBinomialTreeCalibrator : ICalibrator<ImpliedVolatilityTreeCalibrationInput, ImpliedTreeResult>
{
    private const double Epsilon = 1e-6;

    /// <summary>
    /// Calibrates a Rubinstein-style implied binomial tree to the volatility surface.
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
            nameof(ImpliedBinomialTreeCalibrator),
            ImpliedTreeCalibration.StatusFromReversions(built.ReversionCount),
            new Dictionary<string, object?>
            {
                ["steps"] = n,
                ["surfaceSlices"] = input.Surface.Slices.Count,
                ["baseVolatility"] = sigma0,
                ["construction"] = "implied-binomial-forward-induction",
                ["nodeReversions"] = built.ReversionCount
            });

        return new ImpliedTreeResult(tree, built.LocalVolatilities, built.ArrowDebreuPrices, report);
    }

    private static DateOnly ComputeExpiryDate(OptionMarketData market, ImpliedVolatilityTreeCalibrationInput input)
    {
        // Use the longest maturity in the surface to determine the expiry date.
        // Phase 5c: invert the year fraction through the discount curve's day-count
        // convention rather than the ad-hoc 365.25 divisor, so the calibration time axis
        // matches the rest of the pricing stack bit-for-bit.
        double maxMaturity = input.Surface.Slices.Max(s => s.MaturityInYears);
        return market.InvertYearFraction(maxMaturity);
    }
}
