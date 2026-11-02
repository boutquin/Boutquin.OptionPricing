# 09 — Heston Stochastic-Volatility Monte Carlo (Smile)

## Why Black-Scholes Is Not Enough

Black-Scholes prices every strike with the *same* volatility. Real option markets do not:
plot the implied volatility recovered from traded prices against strike and you get a **smile**
(or, for equity indices, a downward-sloping **skew**), not a flat line. The cleanest way to
*generate* a smile from a model — rather than fit one after the fact — is to let volatility
itself be random.

The Heston (1993) model does exactly that. The spot follows a diffusion whose instantaneous
**variance** is itself a mean-reverting random process, correlated with the spot. Negative
correlation makes downside spot moves coincide with volatility spikes, fattening the left tail —
which is precisely the negative skew of index options.

This example uses `SimpleHestonMcPricer`, the deliberately minimal **full-truncation Euler
Monte-Carlo** realisation scoped for v1 (Chriss, *Black-Scholes and Beyond*, §8.5). It is *not*
production Heston — no characteristic-function transform, no finite-difference PDE, no
calibration. It is the bare-minimum pricer that (a) reproduces Black-Scholes in the
zero-vol-of-vol limit and (b) produces a negatively-skewed smile for negative correlation.

**Key types:** `SimpleHestonMcPricer`, `HestonParameters`, `IRandomSource` / `Pcg64RandomSource`, `GaussianSampler`, `BlackScholesPricer` (the smile-inversion oracle)

## The Model

Under the risk-neutral measure the spot `S` and its instantaneous variance `v` evolve jointly:

```
dS_t = (r − q) S_t dt + √(v_t) S_t dW₁
dv_t = κ (θ − v_t) dt + σ_v √(v_t) dW₂
dW₁ dW₂ = ρ dt
```

The five parameters live in `HestonParameters`:

| Symbol | Field | Meaning |
|--------|-------|---------|
| `v₀` | `V0` | initial variance (volatility squared) |
| `κ` | `Kappa` | mean-reversion speed of the variance |
| `θ` | `Theta` | long-run variance level |
| `σ_v` | `SigmaV` | volatility of variance ("vol-of-vol") |
| `ρ` | `Rho` | spot/variance correlation (negative ⇒ negative skew) |

## The Discretization (Full-Truncation Euler)

The simulation advances `N` independent paths over `m` equal steps of `dt = T / m`. Writing
`v⁺ = max(v, 0)`:

```
v_{t+dt} = v_t + κ (θ − v⁺_t) dt + σ_v √(v⁺_t) √dt · Z₂
S_{t+dt} = S_t · exp( (r − q − ½ v⁺_t) dt + √(v⁺_t) √dt · Z₁ )
Z₂ = ρ Z₁ + √(1 − ρ²) Z⊥
```

`Z₁` and `Z⊥` are **independent** standard normals; the correlated variance shock `Z₂` is formed
in-pricer from the two-dimensional correlation identity (no Cholesky factorisation is needed for
the 2×2 case). "Full truncation" applies `v⁺` in the variance drift and diffusion *and* in the
spot's exponent, while letting `v` itself drift slightly negative between steps — the
lowest-bias of the simple Euler variance fixes (Lord, Koekkoek & van Dijk, 2010). It is well
defined in the **non-Feller** regime (`2κθ < σ_v²`), where the variance can touch zero; the
pricer reports `fellerSatisfied` in diagnostics but never gates on it.

### Numerics seam

The normals are drawn from a `GaussianSampler` over an injected `IRandomSource` — no random
number generation is hand-rolled. Because the source is injected and the pricer drives it
**sequentially** (single-threaded by contract), a fixed seed yields a bit-identical,
reproducible price. For parallel pricing, construct one `SimpleHestonMcPricer` per thread, each
with its own seeded source.

## Using It

```csharp
var market = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, dividendYield: 0.0d);
var call   = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);

var parms  = new HestonParameters(V0: 0.04d, Kappa: 1.5d, Theta: 0.04d, SigmaV: 0.6d, Rho: -0.8d);
var pricer = new SimpleHestonMcPricer(parms, paths: 300_000, steps: 64, new Pcg64RandomSource(7UL));

PriceResult result = pricer.Price(call, market);
// result.Price                                        — discounted mean terminal payoff (Money; .Amount for the value)
// result.GetDiagnostics()["standardError"]            — SE of the MC price
// result.GetDiagnostics()["confidenceInterval95"]     — (lower, upper) 95% CI
// result.GetDiagnostics()["fellerSatisfied"]          — 2κθ ≥ σ_v²  (reported, not enforced)
```

The market's flat volatility is **unused** by the Heston pricer — the variance dynamics come
entirely from `HestonParameters`. The discount curve still supplies the risk-free rate `r`, the
dividend curve supplies `q`, and the terminal discount factor discounts the mean payoff.

## Accuracy Contract

Two properties are pinned by the test suite (`SimpleHestonMcPricerTests`), each on **fixed
seeds** so the assertions are deterministic and non-flaky:

- **Zero vol-of-vol ⇒ Black-Scholes.** With `σ_v = 0` and `v₀ = θ`, the variance is frozen and
  the model collapses to geometric Brownian motion at a flat `√θ` vol. The MC mean matches the
  closed-form Black-Scholes price to within Monte-Carlo standard error
  (`ZeroVolOfVol_ReproducesBlackScholes_WithinMonteCarloStandardError`).
- **Negative correlation ⇒ negative skew.** With `ρ < 0`, the Black-Scholes implied volatility
  inverted from the MC prices is **monotonically decreasing in strike**
  (`NegativeCorrelation_ProducesNegativeImpliedVolatilitySkew`).

Three further invariants are asserted: put-call parity holds across the MC call and put prices
within combined standard error; the standard error scales as `1/√N` (quadrupling the path count
halves it); and a fixed seed produces a bit-identical price on repeat. Construction validates the
parameter ranges (`paths ≥ 1`, `steps ≥ 1`, `V0/Kappa/Theta/SigmaV ≥ 0`, `Rho ∈ [−1, 1]`),
throwing `ArgumentOutOfRangeException` on violation.

## Worked Output

Running `--example 24` prices an ATM call in the `σ_v → 0` limit and then a five-strike smile at
`ρ = −0.80, σ_v = 0.60` (spot 100, `r = 5%`, one-year expiry):

```
sigma_v -> 0 check:  BS=10.4506  Heston-MC=10.4694  (±0.0659, 95% CI)
                    within MC error: YES
Negative-correlation smile (rho=-0.80, sigma_v=0.60):
  K=  80  price= 25.4561  implied vol=25.23%
  K=  90  price= 17.2268  implied vol=21.89%
  K= 100  price=  9.9046  implied vol=18.54%
  K= 110  price=  4.1676  implied vol=15.24%
  K= 120  price=  1.0180  implied vol=12.60%
Feller satisfied: False   |   model: Heston-EulerMC
```

The implied volatility falls from 25.23% at `K = 80` to 12.60% at `K = 120` — the downward skew
that flat-vol Black-Scholes cannot produce, generated here directly from the model's negative
spot/variance correlation. The Feller condition is violated (`2·1.5·0.04 = 0.12 < 0.36 = 0.6²`),
which the full-truncation scheme handles without special-casing.

## Limitations

This is the v1 "bare-minimum" Heston, faithful to the roadmap's scope:

- **Monte-Carlo only** — no semi-analytical characteristic-function pricing, no finite-difference
  PDE on the `(S, v)` grid. Prices carry Monte-Carlo noise (reported as `standardError`).
- **No calibration** — parameters are supplied, not fitted to a market surface.
- **No variance reduction** — no antithetic variates or control variates; halving the error
  requires quadrupling the paths.
- **Euler discretization** — the Andersen QE scheme (lower bias at large `σ_v`) is out of scope.

Production-grade Heston (transform pricing, FD, calibration, QE) is scheduled for a later phase;
the shared `HestonParameters` record lives in the domain layer so those models can reuse it.

## Reference

- S. L. Heston, "A Closed-Form Solution for Options with Stochastic Volatility with Applications
  to Bond and Currency Options," *Review of Financial Studies* 6(2), 1993, 327–343.
- R. Lord, R. Koekkoek & D. van Dijk, "A comparison of biased simulation schemes for stochastic
  volatility models," *Quantitative Finance* 10(2), 2010, 177–194 (full truncation).
- N. Chriss, *Black-Scholes and Beyond: Option Pricing Models*, McGraw-Hill, 1997, §8.5.
