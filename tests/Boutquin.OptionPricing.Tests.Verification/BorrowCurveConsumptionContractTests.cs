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
using Boutquin.Numerics.Random;
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Analytics.StochasticVolatility;
using Boutquin.OptionPricing.Calibration.ImpliedTrees;
using Boutquin.OptionPricing.Calibration.Smile;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Exotics.Barriers;
using Boutquin.OptionPricing.Trees.Binomial;
using Boutquin.OptionPricing.Trees.Pricing;
using FluentAssertions;

namespace Boutquin.OptionPricing.Tests.Verification;

/// <summary>
/// Gate-8 tooling backstop for the carry-convention contract:
/// every cost-of-carry-consuming entry point must respond to the borrow curve
/// (<c>effectiveQ = q + b</c>; drift/growth <c>r − q − b</c>).
///
/// A future change that drops the borrow curve from any covered entry point
/// will make the corresponding test go RED deterministically.
///
/// <b>Covered entry points</b>
/// <list type="bullet">
/// <item><see cref="BlackScholesPricer.Price"/> (closed-form BS, effectiveQ = q + b)</item>
/// <item><see cref="BlackScholesGreeks.CalculateGreeks"/> (Greeks use the same effectiveQ)</item>
/// <item><see cref="EuropeanTreePricer"/> via <see cref="CoxRossRubinsteinTreeBuilder"/>
///       (borrow folded into tree risk-neutral probability)</item>
/// <item><see cref="AmericanTreePricer"/> via <see cref="CoxRossRubinsteinTreeBuilder"/>
///       (same tree build path)</item>
/// <item><see cref="SimpleHestonMcPricer"/> (drift r − q − b)</item>
/// <item><see cref="ImpliedVolatilityTreeCalibrator"/> (forward growth exp((r−q−b)·dt))</item>
/// <item><see cref="ImpliedBinomialTreeCalibrator"/> (same forward growth path)</item>
/// <item><see cref="OptionMarketData.ForwardPrice"/> (F = S·DF_div·DF_borrow / DF_disc)</item>
/// <item><see cref="MertonDiscreteDividendPricer"/> (delegates to BS on escrowed market,
///       borrow curves carried through)</item>
/// <item><see cref="BarrierTreePricer"/> (borrow baked into CRR tree risk-neutral probability)</item>
/// <item><see cref="SmileAwareBarrierPricer"/> via implied tree (borrow in calibration forward)</item>
/// </list>
///
/// <b>Maintenance contract:</b> a NEW carry-consuming pricer, calibrator, or forward-price
/// path MUST be added here before the implementing PR is merged.
/// </summary>
/// <remarks>
/// Carry-convention backstop (OPTION-CARRY-BORROW-CONSUMPTION): every cost-of-carry-consuming
/// entry point must respond to the borrow curve (effectiveQ = q + b). A NEW carry-consuming
/// pricer/calibrator MUST be added here.
/// </remarks>
public sealed class BorrowCurveConsumptionContractTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Shared test parameters — two identical markets that differ only in borrow
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly DateOnly s_valuationDate = new(2026, 1, 15);
    private static readonly DateOnly s_expiry = new(2027, 1, 15); // T = 1 year

    private const decimal Spot = 100m;
    private const double RiskFreeRate = 0.05d;
    private const double DividendYield = 0.01d;
    private const double Borrow = 0.03d;
    private const double FlatVol = 0.20d;

    // A vanilla ATM call used by most deterministic tests
    private static EuropeanOption AtmCall => new(CallPut.Call, Spot, Currency.USD, s_expiry);

    // Market without borrow  (b = 0)
    private static OptionMarketData MarketNoBorrow => BuildMarket(borrowRate: 0d);

    // Market with borrow  (b = 0.03)
    private static OptionMarketData MarketWithBorrow => BuildMarket(borrowRate: Borrow);

    /// <summary>Builds a flat-curve market with optional borrow.</summary>
    private static OptionMarketData BuildMarket(double borrowRate)
    {
        var discountCurve = new FlatDiscountCurve(
            new CurveName("USD-OIS"), s_valuationDate, Currency.USD, RiskFreeRate);
        var dividendCurve = new FlatDiscountCurve(
            new CurveName("USD-DIV"), s_valuationDate, Currency.USD, DividendYield);
        var borrowCurve = borrowRate > 0d
            ? new FlatDiscountCurve(new CurveName("USD-BORROW"), s_valuationDate, Currency.USD, borrowRate)
            : null;

        return new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(Spot),
            FlatVol,
            discountCurve,
            dividendCurve,
            borrowCurve);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 1. BlackScholesPricer.Price
    //    Higher borrow → lower effective forward → lower call price.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BlackScholesPricer_Price_RespondsToBorrowCurve()
    {
        // Carry-convention backstop (OPTION-CARRY-BORROW-CONSUMPTION)
        var pricer = new BlackScholesPricer();
        double priceNoBorrow = (double)pricer.Price(AtmCall, MarketNoBorrow).Price.Amount;
        double priceWithBorrow = (double)pricer.Price(AtmCall, MarketWithBorrow).Price.Amount;

        // b > 0 reduces the forward → call price must drop
        priceWithBorrow.Should().BeLessThan(priceNoBorrow,
            because: "a positive borrow rate reduces the forward (effectiveQ = q + b), lowering the call price");

        // The difference should be economically significant (well above rounding):
        // At T = 1, b = 0.03 produces a forward drag of ~3 % which easily exceeds 0.1.
        (priceNoBorrow - priceWithBorrow).Should().BeGreaterThan(0.1d,
            because: "a 3% borrow over 1 year produces a clearly detectable price difference");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. BlackScholesGreeks.CalculateGreeks
    //    Delta: borrow reduces the effective carry discount factor → lower call delta.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BlackScholesGreeks_CalculateGreeks_RespondsToBorrowCurve()
    {
        // Carry-convention backstop (OPTION-CARRY-BORROW-CONSUMPTION)
        var greeks = new BlackScholesGreeks();
        double deltaNoBorrow = greeks.CalculateGreeks(AtmCall, MarketNoBorrow).Delta!.Value;
        double deltaWithBorrow = greeks.CalculateGreeks(AtmCall, MarketWithBorrow).Delta!.Value;

        // Call delta = exp(-effectiveQ * T) * N(d1).  Higher effectiveQ → lower delta.
        deltaWithBorrow.Should().BeLessThan(deltaNoBorrow,
            because: "a positive borrow rate increases effectiveQ, lowering the call delta");

        (deltaNoBorrow - deltaWithBorrow).Should().BeGreaterThan(0.005d,
            because: "a 3% borrow over 1 year produces a detectable delta reduction");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. EuropeanTreePricer via CRR builder
    //    Borrow flows through TreeModelInput → CRR risk-neutral probability.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EuropeanTreePricer_RespondsToBorrowCurve()
    {
        // Carry-convention backstop (OPTION-CARRY-BORROW-CONSUMPTION)
        const int steps = 200; // enough for detectable carry sensitivity

        BinomialTree treeNoBorrow = BuildCrrTree(MarketNoBorrow, AtmCall.Strike.Amount, s_expiry, steps);
        BinomialTree treeWithBorrow = BuildCrrTree(MarketWithBorrow, AtmCall.Strike.Amount, s_expiry, steps);

        var pricer = new EuropeanTreePricer();
        double priceNoBorrow = (double)pricer.Price(AtmCall, MarketNoBorrow, treeNoBorrow).Price.Amount;
        double priceWithBorrow = (double)pricer.Price(AtmCall, MarketWithBorrow, treeWithBorrow).Price.Amount;

        priceWithBorrow.Should().BeLessThan(priceNoBorrow,
            because: "CRR tree bakes borrow into the risk-neutral probability (q = div + borrow); " +
                     "a higher borrow reduces the tree forward and thus the call price");

        (priceNoBorrow - priceWithBorrow).Should().BeGreaterThan(0.1d,
            because: "a 3% borrow over 1 year produces a detectable tree price difference");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. AmericanTreePricer via CRR builder
    //    Same carry path as EuropeanTreePricer; early-exercise makes the delta
    //    even larger, but the borrow direction is identical.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AmericanTreePricer_RespondsToBorrowCurve()
    {
        // Carry-convention backstop (OPTION-CARRY-BORROW-CONSUMPTION)
        const int steps = 200;
        var option = new AmericanOption(CallPut.Call, Spot, Currency.USD, s_expiry);

        BinomialTree treeNoBorrow = BuildCrrTree(MarketNoBorrow, option.Strike.Amount, s_expiry, steps);
        BinomialTree treeWithBorrow = BuildCrrTree(MarketWithBorrow, option.Strike.Amount, s_expiry, steps);

        var pricer = new AmericanTreePricer();
        double priceNoBorrow = (double)pricer.Price(option, MarketNoBorrow, treeNoBorrow).Price.Amount;
        double priceWithBorrow = (double)pricer.Price(option, MarketWithBorrow, treeWithBorrow).Price.Amount;

        priceWithBorrow.Should().BeLessThan(priceNoBorrow,
            because: "American call price inherits the same CRR carry path; borrow reduces the forward " +
                     "and therefore lowers the American call value");

        (priceNoBorrow - priceWithBorrow).Should().BeGreaterThan(0.1d,
            because: "a 3% borrow over 1 year produces a detectable tree price difference");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. SimpleHestonMcPricer.Price
    //    Drift is r − q − b; a positive borrow reduces the forward.
    //    The test uses the zero-vol-of-vol collapse to GBM for a tight MC band.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SimpleHestonMcPricer_Price_RespondsToBorrowCurve()
    {
        // Carry-convention backstop (OPTION-CARRY-BORROW-CONSUMPTION)
        // With sigma_v = 0 and v0 = theta = FlatVol², the Heston process collapses to
        // plain GBM. In that limit the call price equals closed-form BS — so the spread
        // between no-borrow and with-borrow must equal the corresponding BS spread,
        // well above the MC standard error.
        double v0Theta = FlatVol * FlatVol; // 0.04
        var hestonParams = new HestonParameters(V0: v0Theta, Kappa: 1.5d, Theta: v0Theta, SigmaV: 0d, Rho: 0d);
        const int paths = 300_000;
        const int steps = 64;

        var pricerNoBorrow = new SimpleHestonMcPricer(hestonParams, paths, steps, new Pcg64RandomSource(20260101UL));
        var pricerWithBorrow = new SimpleHestonMcPricer(hestonParams, paths, steps, new Pcg64RandomSource(20260101UL));

        var resultNoBorrow = pricerNoBorrow.Price(AtmCall, MarketNoBorrow);
        var resultWithBorrow = pricerWithBorrow.Price(AtmCall, MarketWithBorrow);

        double mcNoBorrow = (double)resultNoBorrow.Price.Amount;
        double mcWithBorrow = (double)resultWithBorrow.Price.Amount;
        double seNoBorrow = (double)resultNoBorrow.GetDiagnostics()["standardError"]!;
        double seWithBorrow = (double)resultWithBorrow.GetDiagnostics()["standardError"]!;
        double combinedSe = Math.Sqrt((seNoBorrow * seNoBorrow) + (seWithBorrow * seWithBorrow));

        // The borrow-induced gap should exceed the combined standard error by a comfortable margin.
        // BS reference gap at these parameters: exp((0.05-0.01-0.03)*1)=exp(0.01*1) vs exp(0.05-0.01)=exp(0.04*1)
        // The actual price difference is ~1.6 units; 3*SE is <0.05 for 300k paths.
        double priceDifference = mcNoBorrow - mcWithBorrow;
        priceDifference.Should().BeGreaterThan(5d * combinedSe,
            because: "the Heston pricer's drift (r-q-b) must respond to borrow; " +
                     $"the borrow-induced gap ({priceDifference:F4}) must be well above 5×SE ({5d * combinedSe:F4})");

        priceDifference.Should().BeGreaterThan(0.5d,
            because: "a 3% borrow over 1 year lowers the Heston call price by ~1.6 units; " +
                     "even with MC noise the gap must exceed 0.5");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. ImpliedVolatilityTreeCalibrator.Calibrate
    //    Forward growth is exp((r − q − b) · dt); a positive borrow lowers the
    //    calibrated root risk-neutral one-step forward.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ImpliedVolatilityTreeCalibrator_Calibrate_RespondsToBorrowCurve()
    {
        // Carry-convention backstop (OPTION-CARRY-BORROW-CONSUMPTION)
        var surface = BuildFlatSurface(FlatVol);
        const int treeSteps = 10;

        var resultNoBorrow = new ImpliedVolatilityTreeCalibrator().Calibrate(
            new ImpliedVolatilityTreeCalibrationInput(surface, treeSteps, MarketNoBorrow));
        var resultWithBorrow = new ImpliedVolatilityTreeCalibrator().Calibrate(
            new ImpliedVolatilityTreeCalibrationInput(surface, treeSteps, MarketWithBorrow));

        resultNoBorrow.IsSuccess.Should().BeTrue();
        resultWithBorrow.IsSuccess.Should().BeTrue();

        // The calibrated root one-step forward E[S_dt] = p*S_up + (1-p)*S_down
        // must be LOWER when borrow is present (growth = exp((r-q-b)*dt)).
        double forwardNoBorrow = RootOneStepForward(resultNoBorrow);
        double forwardWithBorrow = RootOneStepForward(resultWithBorrow);

        forwardWithBorrow.Should().BeLessThan(forwardNoBorrow,
            because: "the implied vol calibrator's forward growth is exp((r-q-b)*dt); " +
                     "a positive borrow reduces the calibrated root one-step forward");

        (forwardNoBorrow - forwardWithBorrow).Should().BeGreaterThan(0.01d,
            because: "a 3% borrow over 1 year produces a detectable forward reduction in the calibrated tree");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. ImpliedBinomialTreeCalibrator.Calibrate
    //    Same forward-induction path as ImpliedVolatilityTreeCalibrator.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ImpliedBinomialTreeCalibrator_Calibrate_RespondsToBorrowCurve()
    {
        // Carry-convention backstop (OPTION-CARRY-BORROW-CONSUMPTION)
        var surface = BuildFlatSurface(FlatVol);
        const int treeSteps = 10;

        var resultNoBorrow = new ImpliedBinomialTreeCalibrator().Calibrate(
            new ImpliedVolatilityTreeCalibrationInput(surface, treeSteps, MarketNoBorrow));
        var resultWithBorrow = new ImpliedBinomialTreeCalibrator().Calibrate(
            new ImpliedVolatilityTreeCalibrationInput(surface, treeSteps, MarketWithBorrow));

        resultNoBorrow.IsSuccess.Should().BeTrue();
        resultWithBorrow.IsSuccess.Should().BeTrue();

        double forwardNoBorrow = RootOneStepForward(resultNoBorrow);
        double forwardWithBorrow = RootOneStepForward(resultWithBorrow);

        forwardWithBorrow.Should().BeLessThan(forwardNoBorrow,
            because: "the implied binomial calibrator's forward growth is exp((r-q-b)*dt); " +
                     "a positive borrow reduces the calibrated root one-step forward");

        (forwardNoBorrow - forwardWithBorrow).Should().BeGreaterThan(0.01d,
            because: "a 3% borrow over 1 year produces a detectable forward reduction in the calibrated tree");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. OptionMarketData.ForwardPrice
    //    F = S · DF_div · DF_borrow / DF_disc = S · exp((r − q − b) · T).
    //    Higher borrow → lower forward.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OptionMarketData_ForwardPrice_RespondsToBorrowCurve()
    {
        // Carry-convention backstop (OPTION-CARRY-BORROW-CONSUMPTION)
        double forwardNoBorrow = MarketNoBorrow.ForwardPrice(s_expiry);
        double forwardWithBorrow = MarketWithBorrow.ForwardPrice(s_expiry);

        forwardWithBorrow.Should().BeLessThan(forwardNoBorrow,
            because: "F = S·exp((r-q-b)·T): borrow cost reduces the cost-of-carry forward");

        // At T=1, b=0.03: the forward drops by forwardNoBorrow * (1 - exp(-b·T)).
        // With r=0.05, q=0.01 → forwardNoBorrow ≈ S·exp(0.04) ≈ 104.08; gap ≈ 3.08.
        double expectedGap = forwardNoBorrow * (1d - Math.Exp(-Borrow * 1d));
        (forwardNoBorrow - forwardWithBorrow).Should().BeApproximately(
            expectedGap,
            precision: 1e-6,
            because: "F_no_borrow - F_with_borrow = F_no_borrow · (1 - exp(-b·T)) " +
                     "because F = S·exp((r-q)·T) vs F = S·exp((r-q-b)·T)");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. MertonDiscreteDividendPricer.Price
    //    Delegates to BlackScholesPricer on the escrowed market; borrow curves
    //    are carried through via the `with` expression, so the effective carry
    //    path is identical to BlackScholesPricer.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MertonDiscreteDividendPricer_Price_RespondsToBorrowCurve()
    {
        // Carry-convention backstop (OPTION-CARRY-BORROW-CONSUMPTION)
        // A single dividend ex-before-expiry ensures the escrowed adjustment fires.
        var dividendPayment = new DividendPayment(s_valuationDate.AddMonths(6), 2m);
        var schedule = new DividendSchedule([dividendPayment]);
        var pricer = new MertonDiscreteDividendPricer(schedule);

        double priceNoBorrow = (double)pricer.Price(AtmCall, MarketNoBorrow).Price.Amount;
        double priceWithBorrow = (double)pricer.Price(AtmCall, MarketWithBorrow).Price.Amount;

        priceWithBorrow.Should().BeLessThan(priceNoBorrow,
            because: "MertonDiscreteDividendPricer delegates to BlackScholesPricer on the escrowed market; " +
                     "the borrow curves are carried through via `with`, so the call price must drop under borrow");

        (priceNoBorrow - priceWithBorrow).Should().BeGreaterThan(0.05d,
            because: "a 3% borrow over 1 year produces a detectable Merton price difference");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 10. BarrierTreePricer
    //     Borrow is baked into the CRR tree's risk-neutral probability; the
    //     barrier pricer consumes the tree, so borrow affects the price.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BarrierTreePricer_RespondsToBorrowCurve()
    {
        // Carry-convention backstop (OPTION-CARRY-BORROW-CONSUMPTION)
        // Down-and-out call: barrier well below spot so the option survives most paths.
        const decimal barrierLevel = 85m;
        const int steps = 200;
        var barrierOption = new BarrierOption(
            CallPut.Call, Spot, Currency.USD,
            barrierLevel, BarrierDirection.Down, BarrierKnockType.Out, s_expiry);

        BinomialTree treeNoBorrow = BuildCrrTree(MarketNoBorrow, Spot, s_expiry, steps);
        BinomialTree treeWithBorrow = BuildCrrTree(MarketWithBorrow, Spot, s_expiry, steps);

        var pricer = new BarrierTreePricer();
        double priceNoBorrow = (double)pricer.Price(barrierOption, MarketNoBorrow, treeNoBorrow).Price.Amount;
        double priceWithBorrow = (double)pricer.Price(barrierOption, MarketWithBorrow, treeWithBorrow).Price.Amount;

        priceWithBorrow.Should().BeLessThan(priceNoBorrow,
            because: "BarrierTreePricer's risk-neutral probability bakes in borrow via the CRR tree; " +
                     "a higher borrow reduces the down-and-out call price");

        (priceNoBorrow - priceWithBorrow).Should().BeGreaterThan(0.05d,
            because: "a 3% borrow over 1 year produces a detectable barrier tree price difference");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 11. SmileAwareBarrierPricer
    //     Borrow flows through the implied-tree calibration forward.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SmileAwareBarrierPricer_RespondsToBorrowCurve()
    {
        // Carry-convention backstop (OPTION-CARRY-BORROW-CONSUMPTION)
        const decimal barrierLevel = 85m;
        const int treeSteps = 10;
        var barrierOption = new BarrierOption(
            CallPut.Call, Spot, Currency.USD,
            barrierLevel, BarrierDirection.Down, BarrierKnockType.Out, s_expiry);

        var surface = BuildFlatSurface(FlatVol);

        var calibNoBorrow = new ImpliedVolatilityTreeCalibrator().Calibrate(
            new ImpliedVolatilityTreeCalibrationInput(surface, treeSteps, MarketNoBorrow));
        var calibWithBorrow = new ImpliedVolatilityTreeCalibrator().Calibrate(
            new ImpliedVolatilityTreeCalibrationInput(surface, treeSteps, MarketWithBorrow));

        calibNoBorrow.IsSuccess.Should().BeTrue();
        calibWithBorrow.IsSuccess.Should().BeTrue();

        var pricer = new SmileAwareBarrierPricer();
        double priceNoBorrow = (double)pricer.Price(barrierOption, MarketNoBorrow, calibNoBorrow).Price.Amount;
        double priceWithBorrow = (double)pricer.Price(barrierOption, MarketWithBorrow, calibWithBorrow).Price.Amount;

        priceWithBorrow.Should().BeLessThan(priceNoBorrow,
            because: "SmileAwareBarrierPricer uses an implied tree calibrated with exp((r-q-b)*dt) forward; " +
                     "borrow reduces the calibrated forward and therefore the down-and-out call price");

        (priceNoBorrow - priceWithBorrow).Should().BeGreaterThan(0.01d,
            because: "a 3% borrow over 1 year produces a detectable smile-aware barrier price difference");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static BinomialTree BuildCrrTree(OptionMarketData market, decimal strike, DateOnly expiry, int steps)
    {
        var input = TreeModelInput.FromMarketData(market, strike, expiry, steps);
        return new CoxRossRubinsteinTreeBuilder().Build(input);
    }

    private static double RootOneStepForward(ImpliedTreeResult result)
    {
        // E[S_dt] = p_up * S_up + (1 - p_up) * S_down at the root node.
        double pUp = result.Tree.ProbabilityLattice![0][0];
        double sUp = result.Tree.SpotLattice[1][0];
        double sDown = result.Tree.SpotLattice[1][1];
        return (pUp * sUp) + ((1d - pUp) * sDown);
    }

    private static VolatilitySurface BuildFlatSurface(double vol)
    {
        var points1 = new List<SurfacePoint>
        {
            new(0.25, 80m, vol), new(0.25, 90m, vol), new(0.25, 100m, vol),
            new(0.25, 110m, vol), new(0.25, 120m, vol)
        };
        var points2 = new List<SurfacePoint>
        {
            new(0.5, 80m, vol), new(0.5, 90m, vol), new(0.5, 100m, vol),
            new(0.5, 110m, vol), new(0.5, 120m, vol)
        };
        return new VolatilitySurface(new[]
        {
            new SmileSlice(0.25, points1),
            new SmileSlice(0.5, points2)
        });
    }
}
