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
using Boutquin.Numerics.Random;
using Boutquin.OptionPricing.Abstractions;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Domain.Results;

namespace Boutquin.OptionPricing.Analytics.StochasticVolatility;

/// <summary>
/// Prices European options under the Heston (1993) stochastic-volatility model by Monte-Carlo
/// simulation with a full-truncation Euler discretization (Chriss,
/// <i>Black-Scholes and Beyond</i>, section 8.5).
/// </summary>
/// <remarks>
/// <para>
/// The model evolves the spot and its instantaneous variance jointly, with the variance
/// following a mean-reverting square-root process correlated with the spot's diffusion (see
/// <see cref="HestonParameters"/>). This pricer is the deliberately minimal "bare-minimum" Euler
/// Monte-Carlo realisation scoped for v1: it reproduces Black-Scholes in the zero-vol-of-vol
/// limit and produces a negatively-skewed implied-volatility smile for negative spot/variance
/// correlation. Production-grade Heston (the semi-analytical characteristic-function transform,
/// finite-difference PDE solving, calibration, and the Andersen QE discretization) is out of
/// scope for this type.
/// </para>
/// <para>
/// <b>Discretization (full-truncation Euler).</b> The simulation advances <c>N</c> independent
/// paths over <c>m</c> equal steps of length <c>dt = T / m</c>. Writing <c>v⁺ = max(v, 0)</c>:
/// </para>
/// <code>
/// v_{t+dt} = v_t + κ (θ − v⁺_t) dt + σ_v √(v⁺_t) √dt · Z₂
/// S_{t+dt} = S_t · exp( (r − q − b − ½ v⁺_t) dt + √(v⁺_t) √dt · Z₁ )
/// Z₂ = ρ Z₁ + √(1 − ρ²) Z⊥
/// </code>
/// <para>
/// where <c>Z₁</c> and <c>Z⊥</c> are independent standard normals and the correlated variance
/// shock <c>Z₂</c> is formed in-pricer (no Cholesky is needed for the two-dimensional case).
/// "Full truncation" — applying <c>v⁺</c> in both the drift and diffusion of the variance and in
/// the spot's exponent while letting <c>v</c> itself go (slightly) negative between steps — is the
/// Lord, Koekkoek &amp; van Dijk (2010) scheme, the lowest-bias of the simple Euler variance fixes.
/// </para>
/// <para>
/// <b>Numerics seam.</b> The independent normals <c>Z₁, Z⊥</c> are drawn from a
/// <see cref="GaussianSampler"/> over an injected <see cref="IRandomSource"/>; no random number
/// generation is hand-rolled. The source is supplied at construction so a fixed seed yields a
/// deterministic, reproducible price.
/// </para>
/// <para>
/// <b>Concurrency.</b> The pricer is single-threaded by contract: a single <see cref="Price"/>
/// call drives the injected <see cref="IRandomSource"/> sequentially over its paths and keeps all
/// mutable state in call-local scratch. <see cref="IRandomSource"/> is not assumed thread-safe; a
/// caller wanting parallel pricing constructs one <see cref="SimpleHestonMcPricer"/> (each with its
/// own seeded source) per thread. Distinct instances are independent, which is what makes the
/// fixed-seed determinism reproducible.
/// </para>
/// <para>
/// <b>Reference:</b> S. L. Heston (1993), <i>Review of Financial Studies</i> 6(2) 327–343;
/// R. Lord, R. Koekkoek &amp; D. van Dijk, "A comparison of biased simulation schemes for
/// stochastic volatility models," <i>Quantitative Finance</i> 10(2), 2010, 177–194 (full
/// truncation); N. Chriss, <i>Black-Scholes and Beyond</i>, McGraw-Hill, 1997, section 8.5.
/// </para>
/// </remarks>
public sealed class SimpleHestonMcPricer
    : IOptionPricer<EuropeanOption, OptionMarketData, PriceResult>
{
    /// <summary>The pricing model identifier stamped on every produced result.</summary>
    private const string Model = "Heston-EulerMC";

    private readonly HestonParameters _parameters;
    private readonly int _paths;
    private readonly int _steps;
    private readonly IRandomSource _rng;

    /// <summary>
    /// Creates a Heston Euler Monte-Carlo pricer.
    /// </summary>
    /// <param name="parameters">The five Heston risk-neutral parameters. Not <see langword="null"/>.</param>
    /// <param name="paths">The number of simulated paths <c>N</c>. Must be at least 1.</param>
    /// <param name="steps">The number of time steps <c>m</c> per path. Must be at least 1.</param>
    /// <param name="rng">
    /// The injected deterministic random source driving the simulation. A fixed seed yields a
    /// reproducible price. Not <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="parameters"/> or <paramref name="rng"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="paths"/> or <paramref name="steps"/> is less than 1, when any of
    /// <c>V0</c>, <c>Kappa</c>, <c>Theta</c>, or <c>SigmaV</c> is negative, or when <c>Rho</c> lies
    /// outside <c>[−1, 1]</c>. The Feller condition (<c>2 κ θ ≥ σ_v²</c>) is deliberately NOT
    /// enforced: full-truncation Euler is well defined in the non-Feller regime.
    /// </exception>
    public SimpleHestonMcPricer(HestonParameters parameters, int paths, int steps, IRandomSource rng)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentOutOfRangeException.ThrowIfLessThan(paths, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(parameters.V0);
        ArgumentOutOfRangeException.ThrowIfNegative(parameters.Kappa);
        ArgumentOutOfRangeException.ThrowIfNegative(parameters.Theta);
        ArgumentOutOfRangeException.ThrowIfNegative(parameters.SigmaV);
        ArgumentOutOfRangeException.ThrowIfLessThan(parameters.Rho, -1d);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(parameters.Rho, 1d);
        if (!double.IsFinite(parameters.V0) || !double.IsFinite(parameters.Kappa)
            || !double.IsFinite(parameters.Theta) || !double.IsFinite(parameters.SigmaV)
            || !double.IsFinite(parameters.Rho))
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "Every Heston parameter must be finite.");
        }

        _parameters = parameters;
        _paths = paths;
        _steps = steps;
        _rng = rng;
    }

    /// <summary>
    /// Prices a European option by simulating the Heston dynamics under the full-truncation Euler
    /// scheme and discounting the mean terminal payoff at the curve-implied risk-free rate.
    /// </summary>
    /// <param name="option">The European option contract (strike, expiry, call/put flag).</param>
    /// <param name="market">
    /// Market snapshot supplying the spot, the discount curve (for the risk-free rate and the
    /// terminal discount factor), and the optional dividend curve (for the carry). The market's
    /// flat volatility is unused — the variance is driven by <see cref="HestonParameters"/>.
    /// </param>
    /// <returns>
    /// A <see cref="PriceResult"/> carrying the discounted mean payoff with
    /// <see cref="CalculationStatus.Success"/>, a <c>ModelName</c> of "Heston-EulerMC", an
    /// assumption summary, and diagnostics for <c>standardError</c>, <c>confidenceInterval95</c>
    /// (a <c>(double lower, double upper)</c> tuple), <c>paths</c>, <c>steps</c>, and
    /// <c>fellerSatisfied</c>. When the option has already expired the intrinsic value is returned
    /// with no simulation. Unrepresentable dynamics, payoffs or statistics return
    /// an unsupported-scenario result with a numeric-range explanation.
    /// </returns>
    /// <exception cref="CurrencyMismatchException">
    /// Thrown when <paramref name="market"/>'s spot currency differs from
    /// <paramref name="option"/>'s contract currency.
    /// </exception>
    public PriceResult Price(EuropeanOption option, OptionMarketData market)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(market);

        if (market.Spot.Currency != option.Currency)
        {
            throw new CurrencyMismatchException(option.Currency, market.Spot.Currency);
        }

        double t = market.TimeToExpiryInYears(option.ExpiryDate);
        if (t <= 0d)
        {
            return new PriceResult(option.IntrinsicValue(market.Spot), null, CalculationStatus.Success, null, Model);
        }

        double s0 = (double)market.Spot.Amount;
        double k = (double)option.Strike.Amount;
        double r = market.RiskFreeRate(option.ExpiryDate);
        double q = market.DividendYield(option.ExpiryDate);
        double b = market.BorrowRate(option.ExpiryDate);
        double discountFactor = market.DiscountFactor(option.ExpiryDate);
        int callSign = option.CallPut switch
        {
            CallPut.Call => 1,
            CallPut.Put => -1,
            _ => throw new NotSupportedException("Unsupported call/put flag.")
        };

        double v0 = _parameters.V0;
        double kappa = _parameters.Kappa;
        double theta = _parameters.Theta;
        double sigmaV = _parameters.SigmaV;
        double rho = _parameters.Rho;

        double dt = t / _steps;
        double sqrtDt = Math.Sqrt(dt);
        // Cost-of-carry: borrow cost reduces the forward (effectiveQ = q + b), matching
        // BlackScholesPricer and the OptionMarketData.ForwardPrice convention.
        double drift = (r - q - b) * dt;
        double rhoComplement = Math.Sqrt(1d - (rho * rho));

        var sampler = new GaussianSampler(_rng);

        // Welford's online mean/variance over the per-path DISCOUNTED payoff. Accumulating the
        // discounted payoff (rather than the undiscounted one) makes the reported standard error
        // and CI the error of the *price* directly. Single-threaded by contract: the sampler is
        // drawn sequentially, so a fixed-seed source yields a reproducible price.
        double mean = 0d;
        double m2 = 0d;
        int n = _paths;

        for (int path = 0; path < n; path++)
        {
            double logS = Math.Log(s0);
            double v = v0;

            for (int step = 0; step < _steps; step++)
            {
                double z1 = sampler.Next();
                double zPerp = sampler.Next();
                double z2 = (rho * z1) + (rhoComplement * zPerp);

                double vPlus = v > 0d ? v : 0d;
                double sqrtVPlus = Math.Sqrt(vPlus);

                // Spot update uses the variance at the START of the step (Euler).
                logS += drift - (0.5d * vPlus * dt) + (sqrtVPlus * sqrtDt * z1);

                // Full-truncation variance update: v_t^+ in both drift and diffusion; v itself may
                // go slightly negative and is truncated again on the next step.
                v += kappa * (theta - vPlus) * dt + sigmaV * sqrtVPlus * sqrtDt * z2;
                if (!double.IsFinite(logS) || !double.IsFinite(v))
                {
                    return PriceResult.Unsupported(Model, "The Euler simulation exceeded the finite numeric range; reduce the parameter scale or time step.", option.Currency);
                }
            }

            double terminal = Math.Exp(logS);
            double payoff = Math.Max(callSign * (terminal - k), 0d);
            double discountedPayoff = payoff * discountFactor;
            if (!double.IsFinite(discountedPayoff) || discountedPayoff >= (double)decimal.MaxValue)
            {
                return PriceResult.Unsupported(Model, "A discounted path payoff exceeded the supported decimal price range.", option.Currency);
            }

            double delta = discountedPayoff - mean;
            mean += delta / (path + 1d);
            m2 += delta * (discountedPayoff - mean);
            if (!double.IsFinite(mean) || !double.IsFinite(m2))
            {
                return PriceResult.Unsupported(Model, "Monte Carlo price statistics exceeded the finite numeric range.", option.Currency);
            }
        }

        // Sample standard error of the mean: s / sqrt(N), where s^2 = m2 / (N - 1).
        double standardError = 0d;
        if (n > 1)
        {
            double sampleVariance = m2 / (n - 1);
            standardError = Math.Sqrt(sampleVariance / n);
        }

        double ciHalfWidth = 1.959963984540054d * standardError; // 97.5th percentile of N(0,1)
        (double Lower, double Upper) confidenceInterval95 = (mean - ciHalfWidth, mean + ciHalfWidth);
        bool fellerSatisfied = 2d * kappa * theta >= sigmaV * sigmaV;

        return new PriceResult(
            option.Currency.Amount(Convert.ToDecimal(mean)),
            null,
            CalculationStatus.Success,
            new Dictionary<string, object?>
            {
                ["standardError"] = standardError,
                ["confidenceInterval95"] = confidenceInterval95,
                ["paths"] = n,
                ["steps"] = _steps,
                ["fellerSatisfied"] = fellerSatisfied,
                ["v0"] = v0,
                ["kappa"] = kappa,
                ["theta"] = theta,
                ["sigmaV"] = sigmaV,
                ["rho"] = rho,
            },
            Model,
            $"Full-truncation Euler Monte-Carlo discretization of the Heston (1993) stochastic-volatility " +
            $"model over {n} paths and {_steps} time steps. Variance follows a mean-reverting square-root " +
            $"process (kappa={kappa:R}, theta={theta:R}, sigma_v={sigmaV:R}) correlated with the spot " +
            $"(rho={rho:R}); full truncation (v^+ = max(v,0)) handles the non-Feller regime. Risk-neutral " +
            $"drift r - q - b (cost-of-carry, effectiveQ = q + b); mean terminal payoff discounted at the " +
            $"curve rate. Feller condition " +
            $"(2*kappa*theta >= sigma_v^2): {(fellerSatisfied ? "satisfied" : "violated")} (reported, not enforced).");
    }
}
