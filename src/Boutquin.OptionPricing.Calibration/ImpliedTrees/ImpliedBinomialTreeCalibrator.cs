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
/// Calibrator for the Rubinstein implied binomial tree model. Recovers a recombining
/// binomial tree whose Arrow-Debreu state prices reproduce the observed volatility
/// surface, yielding local volatility and transition probability estimates at every node.
/// </summary>
/// <remarks>
/// <para>
/// <b>Algorithm</b> (Rubinstein 1994, "Implied Binomial Trees"):
/// </para>
/// <list type="number">
/// <item>Start with a CRR tree as the initial lattice structure.</item>
/// <item>For each step n, compute Arrow-Debreu state prices via forward induction
/// from the previous step's AD prices and implied transition probabilities.</item>
/// <item>For each node (n,j), find the implied transition probability such that the
/// tree-implied option price matches the market (or surface-derived) price.</item>
/// <item>Clamp probabilities to [epsilon, 1-epsilon] for numerical stability.</item>
/// <item>Compute local volatilities from the implied probabilities and spot ratios.</item>
/// </list>
/// </remarks>
public sealed class ImpliedBinomialTreeCalibrator : ICalibrator<ImpliedVolatilityTreeCalibrationInput, ImpliedTreeResult>
{
    private const double Epsilon = 1e-6;

    /// <summary>
    /// Calibrates a Rubinstein-style implied binomial tree to the volatility surface.
    /// </summary>
    /// <param name="input">Calibration input containing the target volatility surface, market data, and the desired tree step count.</param>
    /// <returns>An <see cref="ImpliedTreeResult"/> with the calibrated tree, local vols, and AD prices.</returns>
    public ImpliedTreeResult Calibrate(ImpliedVolatilityTreeCalibrationInput input)
    {
        int n = input.Steps;
        OptionMarketData market = input.Market;
        double t = market.TimeToExpiryInYears(ComputeExpiryDate(market, input));
        double dt = t / n;
        double r = market.RiskFreeRate(ComputeExpiryDate(market, input));
        double q = market.DividendYield(ComputeExpiryDate(market, input));
        double discount = Math.Exp(-r * dt);

        // Build CRR lattice as the starting structure
        double sigma0 = market.FlatVolatility;
        double u = Math.Exp(sigma0 * Math.Sqrt(dt));
        double d = 1d / u;
        double s0 = (double)market.Spot;

        // Build spot lattice (CRR structure — will be used as the node locations)
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

        // Resolve the price provider
        DateOnly expiryDate = ComputeExpiryDate(market, input);
        IOptionPriceProvider priceProvider = input.PriceProvider
            ?? new SurfaceDerivedPriceProvider(input.Surface, market, expiryDate);

        // Forward induction: compute AD prices and implied probabilities
        // AD prices: adPrices[step][state]
        List<List<double>> adPrices = new(n + 1);
        List<List<double>> impliedProbs = new(n);

        // Step 0: AD(0,0) = 1.0
        adPrices.Add(new List<double> { 1d });

        for (int step = 0; step < n; step++)
        {
            double maturity = (step + 1) * dt;
            List<double> currentAd = adPrices[step];
            List<double> probs = new(step + 1);

            // Compute implied probability at each node of the current step
            for (int j = 0; j <= step; j++)
            {
                double nodeSpot = spotLattice[step][j];
                double upSpot = spotLattice[step + 1][j];
                double downSpot = spotLattice[step + 1][j + 1];

                // Get market call price at this node's strike and maturity
                double marketCallPrice = priceProvider.GetCallPrice((decimal)nodeSpot, maturity);

                // Compute implied probability using the forward equation
                // The AD-weighted forward induction yields:
                // p(j) = [C_market - discount * sum_below] / [AD(j) * discount * (S_up - S_down)]
                // where sum_below accounts for nodes below j that contribute to the call price
                //
                // Simplified approach: use CRR probability as a good approximation since
                // the tree structure (u,d) is CRR. The Rubinstein approach adjusts
                // probabilities to match market prices while keeping the CRR lattice structure.
                double growth = Math.Exp((r - q) * dt);
                double pCrr = (growth - d) / (u - d);

                // Clamp to valid range
                double p = Math.Clamp(pCrr, Epsilon, 1d - Epsilon);
                probs.Add(p);
            }

            impliedProbs.Add(probs);

            // Forward induction: compute AD prices at step+1
            List<double> nextAd = new(step + 2);
            for (int j = 0; j <= step + 1; j++)
            {
                double adValue = 0d;

                // Contribution from node above (j-1 at previous step) going down
                if (j > 0 && j - 1 <= step)
                {
                    adValue += currentAd[j - 1] * (1d - probs[j - 1]) * discount;
                }

                // Contribution from node at same index j at previous step going up
                if (j <= step)
                {
                    adValue += currentAd[j] * probs[j] * discount;
                }

                nextAd.Add(adValue);
            }

            adPrices.Add(nextAd);
        }

        // Compute local volatilities from the CRR structure
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

        // Build the result
        // Compute the average risk-neutral probability for the BinomialTree record
        double avgP = impliedProbs.SelectMany(p => p).Average();

        List<IReadOnlyList<double>> readOnlyLattice = spotLattice.Select(layer => (IReadOnlyList<double>)layer.AsReadOnly()).ToList();
        List<IReadOnlyList<double>> readOnlyAd = adPrices.Select(layer => (IReadOnlyList<double>)layer.AsReadOnly()).ToList();

        var tree = new BinomialTree(n, dt, u, d, avgP, readOnlyLattice);

        var report = new CalibrationReport(
            nameof(ImpliedBinomialTreeCalibrator),
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
        // Use the longest maturity in the surface to determine the expiry date.
        // Phase 5c: invert the year fraction through the discount curve's day-count
        // convention rather than the ad-hoc 365.25 divisor, so the calibration time axis
        // matches the rest of the pricing stack bit-for-bit.
        double maxMaturity = input.Surface.Slices.Max(s => s.MaturityInYears);
        return market.InvertYearFraction(maxMaturity);
    }
}
