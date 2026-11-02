# Boutquin.OptionPricing.BlackScholesAndBeyond

Book-aligned executable tracing the full conceptual arc of *Black-Scholes and Beyond: Option Pricing Models* (Neil A. Chriss, Irwin Professional Publishing, 1997). Every chapter group produces console output that maps directly to a section of the book — tables, formulas, and numerical results are annotated with section references (§x.y) so you can read alongside the text.

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

| Ch | Title | Examples | Sections covered |
|----|-------|----------|-----------------|
| 1 | Stocks, Options, and Futures | Security payoff diagrams and arbitrage bounds | §1.7, §1.9, §1.12 |
| 2 | Fundamental Mathematical Concepts | Normal distribution properties and N(x) approximations | §2.1, §2.5, §2.6 |
| 3 | The Geometric Brownian Motion Model of Price Movements | GBM price distribution and volatility calibration | §3.3, §3.4 |
| 4 | The Black-Scholes Formula | Black-Scholes pricing, full Greeks, dividend impact | §4.8, §4.9, §4.10 |
| 5 | More on the Black-Scholes Formula | Delta-hedging P&L simulation | §5.3, §5.6 |
| 6 | Binomial Trees | Arrow-Debreu state prices | §6.11 |
| 7 | Basic Option Pricing with Binomial Trees | CRR convergence, American put early exercise, digital options | §7.4, §7.7, §7.9 |
| 8 | The Volatility Smile | Implied vol recovery across true-vol range; smile slice construction | §8.2, §8.3, §8.4 |
| 9 | Implied Volatility Trees | Derman-Kani implied volatility tree calibration | §9.2, §9.4 |
| 10 | Implied Binomial Trees | Rubinstein implied binomial tree calibration | §10.3 |
| 11 | Pricing Barrier Options in the Presence of the Smile | Flat-vol barrier table, smile-aware pricing, in-out parity | §11.2, §11.3, §11.5 |

## What Each Chapter Produces

### Chapter 1 — Stocks, Options, and Futures
- **§1.7 Security positions:** payoff-at-expiry table for long/short call, put, and stock positions.
- **§1.9 Put-call parity for European options:** the book's exact $0.22 arbitrage example (C=$8.60, P=$3.50).
- **§1.12 Valuing forward contracts:** forward valuation for three underlying types (stock with continuous yield, lumpy dividend, currency).

### Chapter 2 — Fundamental Mathematical Concepts
- **§2.1 Exponential and compound interest:** Table 2.1.1 convergence to eʳ at 10 decimal places; Display 2.1.1 rate conversions.
- **§2.5 Four formulas for N(x):** all four approximations from Displays 2.5.1 and 2.5.2 (Poly1, Poly2, Rational1, Rational2) compared against `CumulativeNormal.Evaluate` (Laikov 2025, ~14 sig. digits).
- **§2.6 Properties of N(x):** symmetry checks N(−x) = 1 − N(x) across all four approximations.

### Chapter 3 — The Geometric Brownian Motion Model of Price Movements
- **§3.3 Calibrating the model:** Table 3.3.1 exact daily prices; 5-step GBM calibration reproducing r̄ ≈ −0.0043 and σ̂ ≈ 23–24% (book: 24.07%).
- **§3.4 The distribution of stock prices:** log-price distribution table with P(S_T ≥ K) = N(d₂).

### Chapter 4 — The Black-Scholes Formula
- **§4.8 Formulas for Δ and B_t:** d₁/d₂ breakdown with N(d₁)/N(d₂); call and put prices; parity verification.
- **§4.9 Black-Scholes with dividends:** dividend-yield impact table (q = 0%–8%).
- **§4.10 Hedge parameters:** full 5-Greek table (Δ, Γ, ν, Θ, ρ) for call and put with parity identities.

### Chapter 5 — More on the Black-Scholes Formula
- **§5.3 Delta hedging / §5.6 Simulated Black-Scholes hedging:** delta-hedging P&L simulation over many paths; mean and standard deviation of hedging error as a percentage of option price.

### Chapter 6 — Binomial Trees
- **§6.11 Arrow-Debreu prices:** state prices at terminal nodes of a recombining CRR tree.

### Chapter 7 — Basic Option Pricing with Binomial Trees
- **§7.4 Pricing European options on multiple-step trees:** CRR convergence table (step counts 1–500) — price vs. Black-Scholes limit.
- **§7.7 Introduction to valuing exotic options:** digital (cash-or-nothing) option — closed-form vs. CRR tree.
- **§7.9 Pricing American options on a flexible tree:** American put vs. European put over nine spot values (70–110) with early-exercise premium column.

### Chapter 8 — The Volatility Smile
- **§8.2–§8.3 Computing implied volatility / Newton-Raphson:** implied-vol recovery across seven true-vol values (10%–40%) with round-trip error in basis points.
- **§8.4 The Volatility Smile:** smile slice construction — quoted strikes plus interpolated intermediate strikes via `MonotoneCubicInterpolator`.

### Chapter 9 — Implied Volatility Trees (Derman-Kani)
- **§9.2 Building implied trees — European options / §9.4 The false position method:** DK forward induction with local-vol distribution by tree step.

### Chapter 10 — Implied Binomial Trees (Rubinstein)
- **§10.3 Sample implied binomial tree:** Rubinstein forward induction — state-probability distribution by tree step.

### Chapter 11 — Pricing Barrier Options in the Presence of the Smile
- **§11.2 In-out parity:** in-out parity table for down-and-in vs. down-and-out.
- **§11.3 The price behavior of barrier options:** flat-vol barrier table with inline KI + KO = vanilla columns.
- **§11.5 Enhanced numerical methods:** smile-aware pricing for four barrier types with skew-impact column.

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
| [`Boutquin.Numerics`](https://github.com/boutquin/Boutquin.Numerics) | `CumulativeNormal.Evaluate` for N(x) comparison in Chapter 2 |
