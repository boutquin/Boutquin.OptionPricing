# Examples

This folder contains five chapter-style walkthroughs that explain the conceptual progression of the library alongside the twenty console programs in `src/Boutquin.OptionPricing.Examples/`. Each walkthrough covers the theory, key types, and code patterns for a group of examples. The console programs produce runnable output — numbers you can verify by hand or compare against a textbook.

Run all twenty examples at once:

```bash
/usr/local/share/dotnet/dotnet run --project src/Boutquin.OptionPricing.Examples --configuration Release
```

---

## Vanilla Pricing → [01-vanilla-pricing.md](01-vanilla-pricing.md)

The foundation. Black-Scholes gives a closed-form price for European options. Before running these, you should understand what `OptionMarketData` is: a market snapshot carrying spot, volatility, and a discount curve — not scalar scalars. The discount factor and dividend yield are derived from curves at expiry.

| Example | What it does |
|---------|-------------|
| **Example 01** — Black-Scholes vanilla pricing | Prices an at-the-money European call using `BlackScholesPricer`. Output: model price under flat-curve market. |
| **Example 02** — Greeks and parity | Computes Delta, Gamma, Vega via `BlackScholesGreeks`, then verifies put-call parity: both routes to the call price must agree. If they differ, either the pricer or the parity implementation is wrong. |
| **Example 18** — Dividend impact on BS | Prices the same call with varying continuous dividend yields (0%–8%). Shows how dividends reduce the forward price, call price, and delta — the cost-of-carry effect from Ch 4.9. |

---

## Tree Methods → [02-tree-convergence.md](02-tree-convergence.md)

Binomial trees discretize the Black-Scholes process. The important insight is that what looks like a different model — a lattice with up/down moves — reproduces the same price as the closed form as steps increase. The break from this convergence is American exercise.

| Example | What it does |
|---------|-------------|
| **Example 03** — Binomial convergence | Prices a European call with a CRR tree at 10, 25, 50, 100, 200, and 500 steps, printing a convergence table with pricing error in basis points vs the analytic BS value. Demonstrates the quantization/specification error from Ch 7.6 — the oscillation between even/odd step counts and the 1/N convergence rate. |
| **Example 04** — American put early exercise | Prices an American put via `AmericanTreePricer` and compares it against the European put. The American price is higher — early exercise premium is always positive for a put on a non-dividend-paying stock. |
| **Example 06** — Bootstrapped curve pricing | Replaces the flat discount curve with a bootstrapped SOFR curve from `CurveBuilder` (fixture-backed — no network required). Shows how the discount factor changes when the curve is term-structured rather than flat. |
| **Example 17** — Arrow-Debreu state prices | Builds a 10-step CRR tree, extracts terminal Arrow-Debreu state prices via `ArrowDebreuStatePriceCalculator`, and verifies they sum to the discount factor e^(-rT). Displays each state's spot and price, then shows the butterfly spread connection (Ch 6.11, 6.13). |

---

## Implied Volatility And Smile → [03-implied-volatility.md](03-implied-volatility.md)

Black-Scholes assumes constant volatility. Markets do not. Implied volatility is what you back out from an observed price. The smile is what you see when you back it out across many strikes: a curve, not a horizontal line. Once you have a smile, you can interpolate it.

| Example | What it does |
|---------|-------------|
| **Example 05** — Implied volatility recovery | Prices a call with `BlackScholesPricer`, then inverts that price with `ImpliedVolatilitySolver` to recover the original volatility. A round-trip sanity check: the recovered vol should match the input to full solver tolerance. |
| **Example 07** — Smile slice construction | Builds a `SmileSlice` from three (strike, IV) quotes and a `VolatilitySurface` from multiple slices. Queries the surface at an interpolated strike. Validates the slice for butterfly and calendar arbitrage using `QuoteArbitrageValidator`. |
| **Example 19** — Digital option on tree | Prices a cash-or-nothing digital call using manual backward induction on the CRR tree (the terminal payoff is $1 if S>K, $0 otherwise). Compares to the BS closed-form e^(-rT)×N(d₂) at multiple step counts. Demonstrates how to extend the tree framework for arbitrary payoffs (Ch 7.7) and exposes the slow convergence caused by the discontinuous payoff. |

---

## Implied Trees → [04-implied-trees.md](04-implied-trees.md)

A flat-vol CRR tree mis-prices barrier options and exotic payoffs because it assumes the same volatility at every strike. Implied trees solve this by calibrating transition probabilities or node spacing to match the market smile. Two classical algorithms exist: Rubinstein's implied binomial tree and Derman-Kani's implied volatility tree.

| Example | What it does |
|---------|-------------|
| **Example 08** — Implied tree comparison | Calibrates both the Derman-Kani tree (`ImpliedVolatilityTreeCalibrator`) and the Rubinstein tree (`ImpliedBinomialTreeCalibrator`) to the same surface. Prints Arrow-Debreu state prices and local volatilities from each. The differences in the wings show where the two methods diverge. |

---

## Barrier Pricing → [05-barrier-pricing.md](05-barrier-pricing.md)

Barrier options are among the most smile-sensitive instruments. The payoff depends on whether the underlying crosses a level — and that crossing probability is exactly what the smile tells you is mispriced by flat-vol assumptions. Run Example 09 first to understand the flat-vol mechanics, then Example 10 to see the skew impact.

| Example | What it does |
|---------|-------------|
| **Example 09** — Barrier option flat-vol | Prices all 8 barrier variants (up/down × in/out × call/put) using `BarrierTreePricer` with a flat-vol CRR tree. Verifies barrier parity: knock-in + knock-out = vanilla for every variant. |
| **Example 10** — Barrier option smile-aware | Prices the same barriers using `SmileAwareBarrierPricer` with a Derman-Kani calibrated tree. Prints flat-vol price, smile-aware price, and the difference — the skew impact. Also demonstrates `BarrierMonitoringAdjustments` (Broadie-Glasserman-Kou) for discrete monitoring. |
| **Example 20** — Barrier in-out parity | Explicitly verifies the fundamental barrier identity V_vanilla = V_KI + V_KO for all 4 direction×call/put families on a 500-step tree. Prints each component price and the parity error (Ch 11.2). |

---

## Delta Hedging

The book's Ch 5 devotes ~20 pages to simulated Black-Scholes hedging — showing that the cost of dynamically replicating an option converges to its theoretical price. This is arguably the most distinctive pedagogical contribution: it proves that the Black-Scholes price is the hedging cost, not an abstract expectation.

| Example | What it does |
|---------|-------------|
| **Example 16** — Delta hedging P&L simulation | Generates 5,000 GBM spot paths, delta-hedges a short call at each daily rebalance using `DeltaHedgeSimulator`, and reports the distribution of hedging error. The mean error is near zero (unbiased hedge); the standard deviation measures discrete rebalancing cost (Ch 5.3, 5.6). |

---

## Market Integration → [curve-layer](../../docs/curve-layer.md)

These examples demonstrate the deeper integration with `Boutquin.Analytics`: curve-group factories, multi-curve discounting, and diagnostic-gated pricing. They use `OptionMarketData.FromCurveGroup()` and `TreeModelInput.FromMarketData()` rather than manual curve extraction and parameter duplication.

| Example | What it does |
|---------|-------------|
| **Example 11** — Term-structure Greeks | Computes Greeks against both a flat curve and a bootstrapped SOFR curve. Compares rho values to show the impact of realistic term structure on rate sensitivity. Uses `FromCurveGroup` with diagnostic gating. |
| **Example 12** — FromCurveGroup factory | Builds a `CurveGroup` with discount and dividend curves, creates `OptionMarketData` via `FromCurveGroup`, and prices. Demonstrates the recommended pattern for multi-curve market construction. |
| **Example 13** — Multi-curve with borrow cost | Adds a borrow-cost curve to the curve group and shows how it affects the cost-of-carry forward price. Compares forward prices with and without the borrow curve. |
| **Example 14** — Diagnostic-gated pricing | Bootstraps a SOFR curve, inspects repricing diagnostics, and only prices if the maximum calibration error is below threshold. Demonstrates the quality-gate pattern for production workflows. |
| **Example 15** — Real market data pricing | Fetches live SOFR fixings from the NY Fed and Treasury par yields from the US Treasury API via `Boutquin.MarketData` adapters. Builds a USD SOFR discount curve from today's real data and prices a call. Requires network access; fails gracefully if APIs are unreachable. Demonstrates the full production pipeline: `AddMarketDataKernel()` + `AddMarketDataNewYorkFed()` + `AddMarketDataUsTreasury()` → `CurveBuilder` → `OptionMarketData.FromCurveGroup()`. |

