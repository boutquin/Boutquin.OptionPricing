# Boutquin.OptionPricing.BlackScholesAndBeyond

Book-aligned executable tracing the full conceptual arc of *Black-Scholes and Beyond: Option Pricing Models* (Neil A. Chriss). Every chapter group produces console output that maps directly to a section of the book — tables, formulas, and numerical results are annotated with section references (§x.y) so you can read alongside the text.

## Running

```bash
# Run all chapters
dotnet run --project src/Boutquin.OptionPricing.BlackScholesAndBeyond

# Run a specific chapter
dotnet run --project src/Boutquin.OptionPricing.BlackScholesAndBeyond -- --chapter 4

# List all chapters
dotnet run --project src/Boutquin.OptionPricing.BlackScholesAndBeyond -- --help
```

## Chapter Map

| Chapter | Title | Examples | Sections |
|---------|-------|----------|----------|
| 1 | Stocks, Options, and Futures | Security payoff diagrams and arbitrage bounds | §1.7, §1.9, §1.12 |
| 2 | Introduction to Pricing | Normal distribution properties and N(x) approximations | §2.1, §2.5, §2.6 |
| 3 | Geometric Brownian Motion | GBM price distribution and volatility calibration | §3.3, §3.4 |
| 4 | The Black-Scholes Formula | Black-Scholes pricing, Greeks, and dividend impact | §4.8, §4.9, §4.10 |
| 5 | Hedging and the Greeks | Delta-hedging P&L simulation | §5.x |
| 6 | Pricing in Practice | Arrow-Debreu state prices | §6.x |
| 7 | Binomial Trees | CRR convergence, American put early exercise, digital options | §7.2, §7.4, §7.7 |
| 8 | Implied Volatility | Implied vol recovery across true-vol range; smile slice construction | §8.3, §8.5 |
| 9 | Implied Volatility Trees | Derman-Kani implied volatility tree calibration | §9.4 |
| 10 | Implied Binomial Trees | Rubinstein implied binomial tree calibration | §10.3 |
| 11 | Barrier Options | Flat-vol barrier table, smile-aware pricing, in-out parity | §11.2, §11.3, §11.5 |

## What Each Chapter Produces

### Chapter 1 — Stocks, Options, and Futures
- Payoff-at-expiry table for long/short call, put, and stock positions.
- Put-call parity: the book's exact $0.22 arbitrage example (C=$8.60, P=$3.50).
- Forward contract valuation for three underlying types (stock with continuous yield, lumpy dividend, currency).

### Chapter 2 — Introduction to Pricing
- Table 2.1.1: compound-interest convergence to eʳ at 10 decimal places.
- Display 2.1.1: rate conversions (continuous, annual, semi-annual).
- All four N(x) approximations from Displays 2.5.1 and 2.5.2 (Poly1, Poly2, Rational1, Rational2) compared to `CumulativeNormal.Evaluate` (Laikov 2025, ~14 sig. digits) with symmetry checks.

### Chapter 3 — Geometric Brownian Motion
- Table 3.3.1: exact daily prices from the book.
- 5-step GBM calibration reproducing r̄ ≈ −0.0043 and σ̂ ≈ 23–24% (book: 24.07%).
- Log-price distribution: P(S_T ≥ K) = N(d₂) table.

### Chapter 4 — The Black-Scholes Formula
- d₁/d₂ breakdown with N(d₁)/N(d₂); call and put prices; parity verification.
- Full 5-Greek table (Δ, Γ, ν, Θ, ρ) for call and put with parity identities.
- Dividend-yield impact table (q = 0%–8%).

### Chapter 5 — Hedging and the Greeks
- Delta-hedging P&L simulation: mean and standard deviation of hedging error as percentage of option price.

### Chapter 6 — Pricing in Practice
- Arrow-Debreu state prices at terminal nodes of a recombining CRR tree.

### Chapter 7 — Binomial Trees
- CRR convergence table (step counts 1–500): price vs. Black-Scholes limit.
- American put: European vs. American comparison over nine spot values (70–110) with early-exercise premium column.
- Digital (cash-or-nothing) option: closed-form formula vs. CRR tree.

### Chapter 8 — Implied Volatility
- Implied-vol recovery across seven true-vol values (10%–40%) with round-trip error in basis points.
- Smile slice construction: quoted strikes plus interpolated intermediate strikes via `MonotoneCubicInterpolator`.

### Chapter 9 — Implied Volatility Trees (Derman-Kani)
- DK forward induction: local-vol distribution by tree step.

### Chapter 10 — Implied Binomial Trees (Rubinstein)
- Rubinstein forward induction: state-probability distribution by tree step.

### Chapter 11 — Barrier Options
- Flat-vol barrier table with inline KI + KO = vanilla parity columns.
- Smile-aware pricing for four barrier types with skew-impact column.
- In-out parity table for down-and-in vs. down-and-out.

## Parameters

Unless noted otherwise, the standard parameter set used across Chapters 4–11 is:

| Parameter | Value |
|-----------|-------|
| Spot (S) | 100 |
| Strike (K) | 100 |
| Time to expiry (T) | 1 year |
| Risk-free rate (r) | 5% |
| Volatility (σ) | 20% |
| Dividend yield (q) | 0% |

This matches the baseline used in *Black-Scholes and Beyond* for cross-chapter consistency.

## Dependencies

| Package | Purpose |
|---------|---------|
| `Boutquin.OptionPricing.Analytics` | `BlackScholesPricer`, `BlackScholesGreeks`, `DeltaHedgeSimulator`, `PutCallParityCalculator` |
| `Boutquin.OptionPricing.Trees` | `CrrBinomialTreeBuilder`, `EuropeanTreePricer`, `AmericanTreePricer`, Arrow-Debreu state prices |
| `Boutquin.OptionPricing.Calibration` | `ImpliedVolatilitySolver`, `SmileSliceBuilder`, `ImpliedVolatilityTreeCalibrator`, `ImpliedBinomialTreeCalibrator` |
| `Boutquin.OptionPricing.Exotics` | `BarrierTreePricer`, `SmileAwareBarrierPricer` |
| `Boutquin.OptionPricing.Domain` | `EuropeanOption`, `AmericanOption`, `BarrierOption`, `OptionMarketData` |
| `Boutquin.OptionPricing.Examples.Shared` | `MarketHelpers.CreateFlatMarket` |
| `Boutquin.Numerics` | `CumulativeNormal.Evaluate` for N(x) comparison in Chapter 2 |
