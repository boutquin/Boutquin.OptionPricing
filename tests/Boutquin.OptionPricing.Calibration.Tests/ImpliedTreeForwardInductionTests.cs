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
using Boutquin.OptionPricing.Calibration.ImpliedTrees;
using Boutquin.OptionPricing.Calibration.Smile;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Calibration.Tests;

/// <summary>
/// Correctness tests for the genuine Derman-Kani implied-tree construction (F01/F02 —
/// replacing the former inert CRR fallback). The construction consumes the smile: it is
/// arbitrage-free (Arrow-Debreu prices sum to the discount factor), it reprices the
/// near-the-money calibration region to finite-tree accuracy, its local-vol lattice is
/// non-uniform and carries the input skew's direction, and a skewed surface yields a
/// genuinely different tree than a flat one (proving the smile is not ignored).
/// </summary>
public sealed class ImpliedTreeForwardInductionTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 1);
    private static readonly DateOnly s_expiryDate = new(2026, 7, 1);
    private const Currency Usd = Currency.USD;

    private static OptionMarketData CreateMarket(double sigma0, double r, double q)
    {
        return new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(100m),
            flatVolatility: sigma0,
            discountCurve: new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, Usd, r),
            dividendCurve: new FlatDiscountCurve(new CurveName("DIV"), s_valuationDate, Usd, q));
    }

    private static VolatilitySurface FlatSurface(double vol) => BuildSurface(_ => vol);

    // Downward (equity-style) skew: implied vol decreases as strike rises.
    private static VolatilitySurface SkewSurface(double atmVol, double slopePerStrike) =>
        BuildSurface(strike => atmVol - (slopePerStrike * ((double)strike - 100d)));

    private static VolatilitySurface BuildSurface(Func<decimal, double> volAt)
    {
        decimal[] strikes = { 70m, 80m, 90m, 100m, 110m, 120m, 130m };
        double[] maturities = { 0.25, 0.5 };
        var points = new List<SurfacePoint>();
        foreach (double tau in maturities)
        {
            foreach (decimal k in strikes)
            {
                points.Add(new SurfacePoint(tau, k, volAt(k)));
            }
        }

        return new VolatilitySurfaceBuilder().Build(points);
    }

    private static double MaxNearMoneyRepricingError(VolatilitySurface surface, OptionMarketData market, int steps)
    {
        var input = new ImpliedVolatilityTreeCalibrationInput(surface, steps, market);
        var provider = new SurfaceDerivedPriceProvider(surface, market, s_expiryDate);
        double t = market.TimeToExpiryInYears(s_expiryDate);

        var result = new ImpliedVolatilityTreeCalibrator().Calibrate(input);
        int n = result.Tree.Steps;
        IReadOnlyList<double> terminalSpots = result.Tree.SpotLattice[n];
        IReadOnlyList<double> terminalAd = result.ArrowDebreuPrices[n];

        double maxError = 0d;
        for (int m = 0; m < terminalSpots.Count; m++)
        {
            double strike = terminalSpots[m];
            if (Math.Abs(strike - 100d) > 15d)
            {
                continue; // deep wings: Derman-Kani is unstable there by construction
            }

            double treeCall = 0d;
            for (int j = 0; j < terminalSpots.Count; j++)
            {
                treeCall += terminalAd[j] * Math.Max(terminalSpots[j] - strike, 0d);
            }

            maxError = Math.Max(maxError, Math.Abs(treeCall - provider.GetCallPrice((decimal)strike, t)));
        }

        return maxError;
    }

    [Fact]
    public void DermanKani_ArrowDebreuSumsToDiscountFactor_EveryStep()
    {
        var market = CreateMarket(0.20, 0.05, 0.0);
        var result = new ImpliedVolatilityTreeCalibrator()
            .Calibrate(new ImpliedVolatilityTreeCalibrationInput(SkewSurface(0.20, 0.002), 12, market));

        double r = market.RiskFreeRate(s_expiryDate);
        double dt = result.Tree.DeltaTime;
        for (int step = 0; step < result.ArrowDebreuPrices.Count; step++)
        {
            result.ArrowDebreuPrices[step].Sum().Should().BeApproximately(Math.Exp(-r * step * dt), 1e-6);
            result.ArrowDebreuPrices[step].Should().OnlyContain(ad => ad >= -1e-12);
        }
    }

    [Fact]
    public void DermanKani_ForwardOfTerminalDistribution_EqualsSpotForward()
    {
        var market = CreateMarket(0.20, 0.05, 0.02);
        var result = new ImpliedVolatilityTreeCalibrator()
            .Calibrate(new ImpliedVolatilityTreeCalibrationInput(SkewSurface(0.20, 0.002), 16, market));

        int n = result.Tree.Steps;
        IReadOnlyList<double> spots = result.Tree.SpotLattice[n];
        IReadOnlyList<double> ad = result.ArrowDebreuPrices[n];
        double r = market.RiskFreeRate(s_expiryDate);
        double q = market.DividendYield(s_expiryDate);
        double t = market.TimeToExpiryInYears(s_expiryDate);

        double discountedForward = 0d;
        for (int j = 0; j < spots.Count; j++)
        {
            discountedForward += ad[j] * spots[j];
        }

        // Σ λ_j S_j = e^{-rT}·E[S_T] = S0·e^{-qT}: the tree is a martingale on the forward.
        discountedForward.Should().BeApproximately(100d * Math.Exp(-q * t), 0.25);
        _ = r;
    }

    [Fact]
    public void DermanKani_FlatSurface_RepricesNearMoneyToFiniteTreeAccuracy()
    {
        var market = CreateMarket(0.20, 0.05, 0.0);

        double maxError = MaxNearMoneyRepricingError(FlatSurface(0.20), market, 20);

        // Finite-tree accuracy comparable to a constant-vol lattice of the same depth.
        maxError.Should().BeLessThan(0.5);
    }

    [Fact]
    public void DermanKani_SmileSurface_ProducesNonUniformLocalVolatilities()
    {
        var market = CreateMarket(0.20, 0.05, 0.0);
        var result = new ImpliedVolatilityTreeCalibrator()
            .Calibrate(new ImpliedVolatilityTreeCalibrationInput(SkewSurface(0.20, 0.002), 16, market));

        IReadOnlyList<double> lastStepVols = result.LocalVolatilities[^1];
        (lastStepVols.Max() - lastStepVols.Min()).Should().BeGreaterThan(1e-3,
            "a skewed surface must produce a non-flat local-vol lattice");
    }

    [Fact]
    public void DermanKani_DownwardSkew_GivesHigherLocalVolAtLowStrikes()
    {
        var market = CreateMarket(0.20, 0.05, 0.0);
        var result = new ImpliedVolatilityTreeCalibrator()
            .Calibrate(new ImpliedVolatilityTreeCalibrationInput(SkewSurface(0.20, 0.002), 16, market));

        // State index 0 = highest spot, last = lowest spot. Compare the near-the-money
        // interior (lower-spot half vs upper-spot half), excluding the unstable wings.
        IReadOnlyList<double> v = result.LocalVolatilities[^1];
        int count = v.Count;
        int q1 = count / 4;             // upper-spot (high-strike, low-vol) region
        int q3 = count - 1 - (count / 4); // lower-spot (low-strike, high-vol) region
        v[q3].Should().BeGreaterThan(v[q1],
            "a downward skew implies higher local volatility at lower spot levels");
    }

    [Fact]
    public void DermanKani_FlatVersusSkew_ProduceDifferentTrees()
    {
        var market = CreateMarket(0.20, 0.05, 0.0);
        var flat = new ImpliedVolatilityTreeCalibrator()
            .Calibrate(new ImpliedVolatilityTreeCalibrationInput(FlatSurface(0.20), 16, market));
        var skew = new ImpliedVolatilityTreeCalibrator()
            .Calibrate(new ImpliedVolatilityTreeCalibrationInput(SkewSurface(0.20, 0.002), 16, market));

        int n = flat.Tree.Steps;
        double maxSpotDiff = 0d;
        for (int j = 0; j <= n; j++)
        {
            maxSpotDiff = Math.Max(maxSpotDiff, Math.Abs(flat.Tree.SpotLattice[n][j] - skew.Tree.SpotLattice[n][j]));
        }

        maxSpotDiff.Should().BeGreaterThan(0.5, "the smile must reshape the terminal lattice (the calibrator is not inert)");
    }
}
