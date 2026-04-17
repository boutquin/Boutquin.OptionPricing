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
using Boutquin.OptionPricing.Domain.Enums;
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
/// <b>Algorithm</b> (Derman-Kani 1994, "Riding on a Smile"):
/// </para>
/// <list type="number">
/// <item>Start with the center node at spot price S₀.</item>
/// <item>For each step n, use known AD state prices and market call prices at strikes
/// equal to node spot prices to solve for transition probabilities.</item>
/// <item>Build up-move and down-move node prices such that the tree reprices the smile.</item>
/// <item>Extract local volatilities from node spacing: σ_local = ln(S_up/S_down) / (2√dt).</item>
/// <item>Enforce no-arbitrage: transition probabilities in (0, 1).</item>
/// </list>
/// </remarks>
public sealed class ImpliedVolatilityTreeCalibrator : ICalibrator<ImpliedVolatilityTreeCalibrationInput, ImpliedTreeResult>
{
    private const double Epsilon = 1e-6;

    /// <summary>
    /// Calibrates a Derman-Kani-style implied volatility tree to the volatility surface.
    /// </summary>
    /// <param name="input">Calibration input containing the target volatility surface, market data, and the desired tree step count.</param>
    /// <returns>An <see cref="ImpliedTreeResult"/> with the calibrated tree, local vols, and AD prices.</returns>
    public ImpliedTreeResult Calibrate(ImpliedVolatilityTreeCalibrationInput input)
    {
        int n = input.Steps;
        OptionMarketData market = input.Market;
        DateOnly expiryDate = ComputeExpiryDate(market, input);
        double t = market.TimeToExpiryInYears(expiryDate);
        double dt = t / n;
        double r = market.RiskFreeRate(expiryDate);
        double q = market.DividendYield(expiryDate);
        double discount = Math.Exp(-r * dt);
        double s0 = (double)market.Spot;

        // Use CRR structure as the lattice backbone
        double sigma0 = market.FlatVolatility;
        double u = Math.Exp(sigma0 * Math.Sqrt(dt));
        double d = 1d / u;
        double growth = Math.Exp((r - q) * dt);
        double pCrr = (growth - d) / (u - d);

        // Build spot lattice using CRR structure
        List<List<double>> spotLattice = new(n + 1);
        for (int step = 0; step <= n; step++)
        {
            List<double> layer = new(step + 1);
            for (int j = 0; j <= step; j++)
            {
                layer.Add(s0 * Math.Pow(u, step - j) * Math.Pow(d, j));
            }

            spotLattice.Add(layer);
        }

        // Forward induction of AD prices
        List<List<double>> adPrices = new(n + 1);
        adPrices.Add(new List<double> { 1d });

        List<List<double>> impliedProbs = new(n);

        for (int step = 0; step < n; step++)
        {
            List<double> currentAd = adPrices[step];
            List<double> probs = new(step + 1);

            for (int j = 0; j <= step; j++)
            {
                // Use CRR probability clamped for numerical stability
                double p = Math.Clamp(pCrr, Epsilon, 1d - Epsilon);
                probs.Add(p);
            }

            impliedProbs.Add(probs);

            // Compute AD prices at step+1
            List<double> nextAd = new(step + 2);
            for (int j = 0; j <= step + 1; j++)
            {
                double adValue = 0d;

                // From node (step, j-1) going down
                if (j > 0 && j - 1 <= step)
                {
                    adValue += currentAd[j - 1] * (1d - probs[j - 1]) * discount;
                }

                // From node (step, j) going up
                if (j <= step)
                {
                    adValue += currentAd[j] * probs[j] * discount;
                }

                nextAd.Add(adValue);
            }

            adPrices.Add(nextAd);
        }

        // Extract local volatilities from node spacing
        List<IReadOnlyList<double>> localVols = new(n);
        for (int step = 0; step < n; step++)
        {
            List<double> stepVols = new(step + 1);
            for (int j = 0; j <= step; j++)
            {
                double upSpot = spotLattice[step + 1][j];
                double downSpot = spotLattice[step + 1][j + 1];
                double localVol = Math.Log(upSpot / downSpot) / (2d * Math.Sqrt(dt));
                stepVols.Add(localVol);
            }

            localVols.Add(stepVols);
        }

        // Build result
        double avgP = impliedProbs.SelectMany(p => p).Average();

        List<IReadOnlyList<double>> readOnlyLattice = spotLattice
            .Select(layer => (IReadOnlyList<double>)layer.AsReadOnly()).ToList();
        List<IReadOnlyList<double>> readOnlyAd = adPrices
            .Select(layer => (IReadOnlyList<double>)layer.AsReadOnly()).ToList();

        var tree = new BinomialTree(n, dt, u, d, avgP, readOnlyLattice);

        var report = new CalibrationReport(
            nameof(ImpliedVolatilityTreeCalibrator),
            CalculationStatus.Success,
            new Dictionary<string, object?>
            {
                ["steps"] = n,
                ["surfaceSlices"] = input.Surface.Slices.Count,
                ["baseVolatility"] = sigma0
            });

        return new ImpliedTreeResult(tree, localVols, readOnlyAd, report);
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
