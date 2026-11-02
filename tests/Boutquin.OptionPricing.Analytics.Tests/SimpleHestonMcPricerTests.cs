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
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using FluentAssertions;

namespace Boutquin.OptionPricing.Analytics.Tests;

public sealed class SimpleHestonMcPricerTests
{
    private static readonly DateOnly s_valuationDate = new(2026, 1, 15);

    // A flat 20% vol "matching" Heston configuration: variance v0 = theta = 0.04 (sigma = 0.20),
    // used by the BS-limit / parity / determinism tests.
    private static HestonParameters FlatVarianceParameters(double sigmaV, double rho) =>
        new(V0: 0.04d, Kappa: 1.5d, Theta: 0.04d, SigmaV: sigmaV, Rho: rho);

    [Fact]
    public void ZeroVolOfVol_ReproducesBlackScholes_WithinMonteCarloStandardError()
    {
        // AC-1 (G6): with sigma_v = 0 and v0 = theta, the variance is frozen at a constant 0.04,
        // so the Heston process collapses to geometric Brownian motion at a flat 20% vol. The MC
        // mean price must agree with the closed-form Black-Scholes price to within a few Monte-Carlo
        // standard errors (we allow 3 SE — a ~99.7% band for an unbiased estimator).
        var market = CreateMarket(spot: 100m, flatVol: 0.20d, riskFreeRate: 0.05d);
        var expiry = s_valuationDate.AddDays(365);
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);

        var bsPrice = (double)new BlackScholesPricer().Price(call, market).Price.Amount;

        var pricer = new SimpleHestonMcPricer(
            FlatVarianceParameters(sigmaV: 0d, rho: 0d), paths: 200_000, steps: 64, new Pcg64RandomSource(20260101UL));
        var result = pricer.Price(call, market);

        result.Status.Should().Be(CalculationStatus.Success);
        double mc = (double)result.Price.Amount;
        double se = (double)result.GetDiagnostics()["standardError"]!;

        se.Should().BeGreaterThan(0d);
        Math.Abs(mc - bsPrice).Should().BeLessThan(3d * se,
            because: "with zero vol-of-vol the Heston MC mean must match closed-form Black-Scholes within Monte-Carlo error");
    }

    [Fact]
    public void ZeroVolOfVol_WithBorrowCurve_ReproducesBlackScholes_WithinMonteCarloStandardError()
    {
        // Borrow-curve carry seam (fp ce75c43e / 6bb8826f): the library-wide cost-of-carry
        // convention is effectiveQ = q + b, so the Heston risk-neutral asset drift must be
        // (r - q - b), exactly like BlackScholesPricer. In the frozen-variance limit (sigma_v = 0,
        // v0 = theta) the Heston process collapses to GBM at a flat 20% vol, so with a NON-ZERO
        // borrow curve the MC mean must still agree with closed-form Black-Scholes (which already
        // honours q + b) to within a few Monte-Carlo standard errors. If the drift omits b, the
        // Heston forward is too high (no borrow drag), inflating the call price away from BS by
        // far more than the MC band — the ~1.8 gap Critic A measured.
        var market = CreateMarketWithBorrow(
            spot: 100m, flatVol: 0.20d, riskFreeRate: 0.05d, dividendYield: 0.02d, borrowRate: 0.03d);
        var expiry = s_valuationDate.AddDays(365);
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);

        var bsPrice = (double)new BlackScholesPricer().Price(call, market).Price.Amount;

        var pricer = new SimpleHestonMcPricer(
            FlatVarianceParameters(sigmaV: 0d, rho: 0d), paths: 200_000, steps: 64, new Pcg64RandomSource(20260101UL));
        var result = pricer.Price(call, market);

        result.Status.Should().Be(CalculationStatus.Success);
        double mc = (double)result.Price.Amount;
        double se = (double)result.GetDiagnostics()["standardError"]!;

        se.Should().BeGreaterThan(0d);
        Math.Abs(mc - bsPrice).Should().BeLessThan(3d * se,
            because: "with a non-zero borrow curve the Heston drift must use the library carry convention r - q - b, " +
                     "so the zero-vol-of-vol MC mean matches closed-form Black-Scholes within Monte-Carlo error");
    }

    [Fact]
    public void NegativeCorrelation_ProducesNegativeImpliedVolatilitySkew()
    {
        // AC-2 (G7): with rho < 0, downside spot moves are accompanied by variance increases, fattening
        // the left tail of the terminal distribution. Out-of-the-money PUTS (low strikes) therefore carry
        // a higher implied volatility than out-of-the-money calls (high strikes) — a negative skew. We
        // price calls across an ascending strike grid, invert each MC price to its Black-Scholes implied
        // volatility, and assert the implied-vol curve is monotonically DECREASING in strike (skew sign
        // negative). A shared seed per strike keeps the comparison clean of cross-strike MC noise.
        var market = CreateMarket(spot: 100m, flatVol: 0.20d, riskFreeRate: 0.05d);
        var expiry = s_valuationDate.AddDays(365);
        var parms = FlatVarianceParameters(sigmaV: 0.6d, rho: -0.8d);

        decimal[] strikes = [80m, 90m, 100m, 110m, 120m];
        double[] impliedVols = new double[strikes.Length];

        for (int i = 0; i < strikes.Length; i++)
        {
            var call = new EuropeanOption(CallPut.Call, strikes[i], Currency.USD, expiry);
            // Same seed at every strike: the strike grid is compared on a common random stream.
            var result = new SimpleHestonMcPricer(parms, paths: 300_000, steps: 64, new Pcg64RandomSource(7UL))
                .Price(call, market);
            impliedVols[i] = ImplyBlackScholesVol(call, market, (double)result.Price.Amount);
        }

        // Negative skew: implied vol strictly decreases from the lowest strike to the highest.
        for (int i = 1; i < impliedVols.Length; i++)
        {
            impliedVols[i].Should().BeLessThan(impliedVols[i - 1],
                because: $"rho < 0 must produce a negative IV skew: IV at K={strikes[i]} should be below IV at K={strikes[i - 1]} " +
                         $"(got {impliedVols[i]:F4} vs {impliedVols[i - 1]:F4})");
        }
    }

    [Fact]
    public void StandardError_ScalesAsInverseSqrtOfPathCount()
    {
        // AC-4: the Monte-Carlo standard error of the mean is s / sqrt(N), so quadrupling the path
        // count must halve the standard error. We price the SAME option at N and 4N paths (each on its
        // own seeded source) and assert SE(4N) ~ SE(N)/2 within a tolerance band that accommodates the
        // sampling variation of the sample-variance estimate itself.
        var market = CreateMarket(spot: 100m, flatVol: 0.20d, riskFreeRate: 0.05d);
        var expiry = s_valuationDate.AddDays(365);
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);
        var parms = FlatVarianceParameters(sigmaV: 0.4d, rho: -0.5d);

        const int n = 50_000;
        double seN = (double)new SimpleHestonMcPricer(parms, paths: n, steps: 48, new Pcg64RandomSource(101UL))
            .Price(call, market).GetDiagnostics()["standardError"]!;
        double se4N = (double)new SimpleHestonMcPricer(parms, paths: 4 * n, steps: 48, new Pcg64RandomSource(202UL))
            .Price(call, market).GetDiagnostics()["standardError"]!;

        double ratio = se4N / seN;
        // Ideal ratio is 0.5; allow a ±20% band (0.40–0.60) for finite-sample variation.
        ratio.Should().BeInRange(0.40d, 0.60d,
            because: $"standard error must scale as 1/sqrt(N): SE(4N)/SE(N) should be ~0.5 (got {ratio:F3})");
    }

    [Fact]
    public void PutCallParity_HoldsAcrossMonteCarloPrices_WithinStandardError()
    {
        // AC-3: the discounted-mean-payoff MC estimator is unbiased and consistent with the
        // forward, so put-call parity C - P = S*DF_div - K*DF_disc must hold across the two MC
        // prices to within the combined standard error of the call and put estimates. Both legs
        // are priced on independently seeded sources at the SAME (sigma_v, rho), so the parity
        // residual is pure Monte-Carlo noise.
        var market = CreateMarket(spot: 100m, flatVol: 0.20d, riskFreeRate: 0.05d);
        var expiry = s_valuationDate.AddDays(365);
        var call = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);
        var put = new EuropeanOption(CallPut.Put, 100m, Currency.USD, expiry);

        var parms = FlatVarianceParameters(sigmaV: 0.4d, rho: -0.5d);

        var callResult = new SimpleHestonMcPricer(parms, paths: 200_000, steps: 64, new Pcg64RandomSource(11UL))
            .Price(call, market);
        var putResult = new SimpleHestonMcPricer(parms, paths: 200_000, steps: 64, new Pcg64RandomSource(22UL))
            .Price(put, market);

        double c = (double)callResult.Price.Amount;
        double p = (double)putResult.Price.Amount;
        double seC = (double)callResult.GetDiagnostics()["standardError"]!;
        double seP = (double)putResult.GetDiagnostics()["standardError"]!;
        double seCombined = Math.Sqrt((seC * seC) + (seP * seP));

        // Forward-parity right-hand side: S*DF_div - K*DF_disc.
        double s = (double)market.Spot.Amount;
        double kStrike = (double)call.Strike.Amount;
        double parityRhs = (s * market.DividendDiscountFactor(expiry)) - (kStrike * market.DiscountFactor(expiry));

        Math.Abs((c - p) - parityRhs).Should().BeLessThan(3d * seCombined,
            because: "Heston MC call and put prices must satisfy put-call parity within Monte-Carlo error");
    }

    [Fact]
    public void FixedSeed_ProducesDeterministicReproduciblePrice()
    {
        // AC-5: the pricer is single-threaded by contract and draws the injected source
        // sequentially, so two pricers built with equally-seeded sources must return the
        // bit-identical price (and standard error) for the same option and market.
        var market = CreateMarket(spot: 100m, flatVol: 0.20d, riskFreeRate: 0.05d);
        var expiry = s_valuationDate.AddDays(365);
        var call = new EuropeanOption(CallPut.Call, 105m, Currency.USD, expiry);
        var parms = FlatVarianceParameters(sigmaV: 0.5d, rho: -0.7d);

        var first = new SimpleHestonMcPricer(parms, paths: 50_000, steps: 48, new Pcg64RandomSource(424242UL))
            .Price(call, market);
        var second = new SimpleHestonMcPricer(parms, paths: 50_000, steps: 48, new Pcg64RandomSource(424242UL))
            .Price(call, market);

        second.Price.Should().Be(first.Price);
        ((double)second.GetDiagnostics()["standardError"]!).Should().Be((double)first.GetDiagnostics()["standardError"]!);
    }

    [Fact]
    public void InvalidConstruction_ThrowsArgumentOutOfRangeException()
    {
        // AC-6: the five Heston parameters and the (paths, steps) discretization counts are
        // model configuration; an out-of-range value is a programmer error, so construction must
        // throw ArgumentOutOfRangeException (not produce an UnsupportedScenario at price time).
        var rng = new Pcg64RandomSource(1UL);
        var ok = FlatVarianceParameters(sigmaV: 0.3d, rho: -0.5d);

        // paths <= 0
        FluentActions.Invoking(() => new SimpleHestonMcPricer(ok, paths: 0, steps: 10, rng))
            .Should().Throw<ArgumentOutOfRangeException>();
        // steps <= 0
        FluentActions.Invoking(() => new SimpleHestonMcPricer(ok, paths: 100, steps: 0, rng))
            .Should().Throw<ArgumentOutOfRangeException>();
        // V0 < 0
        FluentActions.Invoking(() => new SimpleHestonMcPricer(ok with { V0 = -0.01d }, paths: 100, steps: 10, rng))
            .Should().Throw<ArgumentOutOfRangeException>();
        // Kappa < 0
        FluentActions.Invoking(() => new SimpleHestonMcPricer(ok with { Kappa = -0.01d }, paths: 100, steps: 10, rng))
            .Should().Throw<ArgumentOutOfRangeException>();
        // Theta < 0
        FluentActions.Invoking(() => new SimpleHestonMcPricer(ok with { Theta = -0.01d }, paths: 100, steps: 10, rng))
            .Should().Throw<ArgumentOutOfRangeException>();
        // SigmaV < 0
        FluentActions.Invoking(() => new SimpleHestonMcPricer(ok with { SigmaV = -0.01d }, paths: 100, steps: 10, rng))
            .Should().Throw<ArgumentOutOfRangeException>();
        // Rho < -1
        FluentActions.Invoking(() => new SimpleHestonMcPricer(ok with { Rho = -1.01d }, paths: 100, steps: 10, rng))
            .Should().Throw<ArgumentOutOfRangeException>();
        // Rho > 1
        FluentActions.Invoking(() => new SimpleHestonMcPricer(ok with { Rho = 1.01d }, paths: 100, steps: 10, rng))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    private static OptionMarketData CreateMarket(decimal spot, double flatVol, double riskFreeRate)
    {
        return new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(spot),
            flatVol,
            new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, Currency.USD, riskFreeRate));
    }

    private static OptionMarketData CreateMarketWithBorrow(
        decimal spot, double flatVol, double riskFreeRate, double dividendYield, double borrowRate)
    {
        return new OptionMarketData(
            s_valuationDate,
            Currency.USD.Amount(spot),
            flatVol,
            new FlatDiscountCurve(new CurveName("USD-OIS"), s_valuationDate, Currency.USD, riskFreeRate),
            new FlatDiscountCurve(new CurveName("USD-DIV"), s_valuationDate, Currency.USD, dividendYield),
            new FlatDiscountCurve(new CurveName("USD-BORROW"), s_valuationDate, Currency.USD, borrowRate));
    }

    /// <summary>
    /// Inverts a European option price to its Black-Scholes implied volatility by bisection over
    /// the closed-form <see cref="BlackScholesPricer"/>. The pricer is monotone increasing in
    /// volatility, so bisection on [1e-4, 5.0] converges to the implied vol. Used only by the
    /// negative-skew test to map MC prices onto an IV curve.
    /// </summary>
    private static double ImplyBlackScholesVol(EuropeanOption option, OptionMarketData market, double targetPrice)
    {
        var pricer = new BlackScholesPricer();
        double lo = 1e-4d;
        double hi = 5.0d;

        for (int iter = 0; iter < 200; iter++)
        {
            double mid = 0.5d * (lo + hi);
            double price = (double)pricer.Price(option, market.WithFlatVolatility(mid)).Price.Amount;
            if (price > targetPrice)
            {
                hi = mid;
            }
            else
            {
                lo = mid;
            }
        }

        return 0.5d * (lo + hi);
    }
}
