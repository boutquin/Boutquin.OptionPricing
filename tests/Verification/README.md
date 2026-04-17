# Cross-Language Verification Vectors

Python reference implementations of OptionPricing kernels. Each generator
emits a JSON file in `vectors/` that the C# test project
`Boutquin.OptionPricing.Tests.Verification` consumes to assert
cross-language agreement within tiered tolerances.

## Layout

| File | Covers | Reference |
|------|--------|-----------|
| `generate_blackscholes_vectors.py` | BlackScholesPricer, BlackScholesGreeks, put-call parity prices | scipy.stats.norm closed form |
| `generate_parity_vectors.py` | PutCallParity.ImpliedCallPrice/ImpliedPutPrice, RiskNeutralValuation.ForwardPrice | Pure algebra |
| `generate_binomial_vectors.py` | CRR, Jarrow-Rudd, Flexible wrapper, European + American tree pricers, convergence to BSM, Arrow-Debreu terminal state prices | Python replica of the C# tree algorithms |
| `generate_iv_vectors.py` | ImpliedVolatilitySolver round-trip | scipy.optimize.brentq on scipy BSM |
| `generate_smile_vectors.py` | VolatilitySurfaceBuilder structural ordering, GetVol grid reads, two-node linear reductions, QuoteArbitrageValidator | Structural + grid expectations |
| `generate_barrier_vectors.py` | BarrierParity identity, BGK monitoring correction, BarrierTreePricer | Python replica of BarrierTreePricer algorithm + BGK formula |
| `generate_impliedtree_vectors.py` | ImpliedBinomialTreeCalibrator (Rubinstein) + ImpliedVolatilityTreeCalibrator (Derman-Kani) | Python replica of the CRR-backed lattice the current C# impls actually build |
| `generate_hedging_vectors.py` | DeltaHedgeSimulator along pre-specified spot paths | Python replica of the C# rebalance loop, including FlatDiscountCurve's time-stretched-rate quirk |

## Regenerating

```sh
pip install -r requirements.txt
python generate_vectors.py
```

Vectors are checked in; regenerate whenever the C# pricers or the reference
conventions change. The C# csproj (`Content Include`) copies the JSON files
into the test output directory at build time.

## Precision Tiers

Defined in `conftest.py` and mirrored in `CrossLanguageVerificationBase.cs`.

| Tier | Tolerance | Use |
|------|-----------|-----|
| `PRECISION_EXACT` | 1e-10 | Pure algebra, parity identities |
| `PRECISION_NUMERIC` | 1e-6 | Closed-form with normal CDF; identical-algorithm tree or path walk |
| `PRECISION_STATISTICAL` | 1e-4 | Monte Carlo envelopes, tree → analytic-limit |

## Convention Notes

- **Year fractions**: Actual/365Fixed. Generators emit `days_to_expiry` and the
  C# side computes `expiry = valuationDate.AddDays(days_to_expiry)`.
- **Greeks scaling**: vega and rho per 1 percentage-point; theta per calendar day.
- **Borrow handling** (Hull §19.12): single effective carry `q_eff = q + b`
  used symmetrically in `d1`, `d2`, every `e^{-qT}` factor, and the
  `q S N(d1)` drift term in theta.
- **Tree carry**: the C# tree builders use `q = DividendYield() - BorrowRate()`.
  The Python reference matches.
- **Hedging simulator** inherits a time-stretched effective rate at each
  rebalance step because the `FlatDiscountCurve` inside the cloned market
  keeps its original `ValuationDate` — see the `r_eff = r * T_total / T_remaining`
  block in `generate_hedging_vectors.py:_simulate`. Python replicates the
  stretch to stay bit-aligned with the C# output.
- **Implied trees**: both C# calibrators currently build a CRR-backed lattice
  with clamped CRR probabilities; the verifier pins down that actual behavior
  rather than the abstract Rubinstein / Derman-Kani paper algorithms.
- **IV solver scenarios** filter out deep-ITM/OTM cases whose time value is
  below 1 cent — in those regimes vega is near zero and the C# `BrentSolver`
  legitimately returns the bracket lower bound rather than converging on a
  meaningful σ. The filter is applied in the generator, not at assertion time.

## What the verifier does NOT cover

- `SmileAwareBarrierPricer` — same backward-induction logic as `BarrierTreePricer`
  applied to a calibrated `ImpliedTreeResult`'s tree. Because both implied-tree
  calibrators produce a CRR-structured lattice, the behavior is covered
  transitively by `BarrierTreePricer` on a CRR tree + the implied-tree
  verifier's lattice-structure assertions.
- Full bivariate interpolation of `VolatilitySurface.GetVol` off-grid —
  uses Boutquin.Numerics-specific schemes (arXiv:2402.01324 monotone-cubic
  Hermite + arXiv:2507.05083 modified-not-a-knot cubic spline) that have no
  scipy equivalent. Grid reads and two-node linear reductions are verified;
  off-grid bivariate interpolation remains to be pinned down via a
  Numerics-side verifier.
