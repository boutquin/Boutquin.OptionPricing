# Boutquin.OptionPricing

[![NuGet](https://img.shields.io/nuget/v/Boutquin.OptionPricing.Domain.svg)](https://www.nuget.org/packages/Boutquin.OptionPricing.Domain)
[![License](https://img.shields.io/github/license/boutquin/Boutquin.OptionPricing)](https://github.com/boutquin/Boutquin.OptionPricing/blob/v2.0.0/LICENSE.txt)
[![Build](https://github.com/boutquin/Boutquin.OptionPricing/actions/workflows/pr-verify.yml/badge.svg)](https://github.com/boutquin/Boutquin.OptionPricing/actions/workflows/pr-verify.yml)

> **2.0.0:** targets .NET 10, `Boutquin.Curves` 2.0.0, `Boutquin.MarketData` 2.0.1 and `Boutquin.Numerics` 2.0.0 from NuGet.org. See the [changelog](https://github.com/boutquin/Boutquin.OptionPricing/blob/v2.0.0/CHANGELOG.md).

`Boutquin.OptionPricing` is a concept-first, model-oriented C# library that maps the full conceptual arc of *Black-Scholes and Beyond: Option Pricing Models* into a clean, testable, modular codebase. It is designed as a companion to your reading of the book — every package corresponds to a chapter group, every model carries the assumptions stated in the text, and every example traces the progression from closed-form pricing through tree methods to calibrated implied surfaces.

The library depends on [`Boutquin.Curves`](https://github.com/boutquin/Boutquin.Curves) for all curve infrastructure: discount factors, forward rates, dividend curves, and carry. This keeps the option-pricing layer free from data-fetching and bootstrapping concerns. A flat-curve helper is provided for quick exploration; a bootstrapped curve from the NY Fed or Bank of Canada fixtures is one call away for production-grade discounting.

The design is production-grade in engineering terms — strict code quality, full test coverage, documented XML API surface — while remaining concept-first in scope. This is not a trading system or a brokerage integration. It is a reference implementation: the kind you study alongside a textbook and then use as the starting point for your own pricing work.

## What You Will Learn

Working through this codebase alongside *Black-Scholes and Beyond* gives practical exposure to:

- why risk-neutral pricing works and how the discount curve enters the Black-Scholes formula via `OptionMarketData` rather than a scalar rate,
- how binomial trees converge to Black-Scholes as steps increase, and where American exercise breaks that convergence,
- how to invert a model price to recover implied volatility — and why bisection, Newton-Raphson, and secant solvers behave differently near the boundary,
- how a smile surface is constructed from market quotes and what butterfly/calendar arbitrage constraints enforce,
- how implied binomial trees (Rubinstein) and implied volatility trees (Derman-Kani) fit a calibrated surface and price path-dependent instruments like barriers.

## Book-To-Code Map

| Book chapter group | Package |
|-------------------|---------|
| Instruments, arbitrage, parity | `Domain`, `Analytics` |
| Probability and distributions | `Numerics` |
| Geometric Brownian motion, Black-Scholes derivation | `Analytics` |
| Hedging interpretation | `Analytics` |
| Binomial trees | `Trees` |
| American exercise and tree pricing | `Trees` |
| Implied volatility and smile | `Calibration` |
| Implied volatility trees (Derman-Kani) | `Calibration` |
| Implied binomial trees (Rubinstein) | `Calibration` |
| Barrier options in the presence of the smile | `Exotics` |
| Discrete dividends (escrowed-dividend / Merton) | `Analytics` |
| Hedge parameters from tree lattices (Δ, Γ, Θ) | `Trees` |
| Stochastic volatility (Heston Euler MC) | `Analytics` |

## Packages

Packages are organized in dependency order, from foundational contracts through advanced models.

**Contracts**

The foundational contracts all other packages depend on. Downstream code that only consumes prices and Greeks will typically reference only this group.

| Package | Description |
|---------|-------------|
| `Boutquin.OptionPricing.Abstractions` | Generic pricing, Greek, calibration, and diagnostic interfaces |
| `Boutquin.OptionPricing.Domain` | Option contracts (`EuropeanOption`, `AmericanOption`, `BarrierOption`), `OptionMarketData`, result types (`PriceResult`, `GreekSet`) |

**Vanilla Pricing Models**

Closed-form and tree-based pricing for European and American options.

| Package | Description |
|---------|-------------|
| `Boutquin.OptionPricing.Analytics` | `BlackScholesPricer`, `BlackScholesGreeks`, `BlackScholesDividendPricer`, `MertonDiscreteDividendPricer`, `SimpleHestonMcPricer`, `DeltaHedgeSimulator`, put-call parity and risk-neutral valuation |
| `Boutquin.OptionPricing.Trees` | CRR, Jarrow-Rudd, and flexible binomial tree builders; `EuropeanTreePricer`, `AmericanTreePricer`, `TreeGreeksCalculator`; Arrow-Debreu state prices |

**Smile, Calibration, And Exotics**

Surface fitting, implied tree calibration, and barrier pricing.

| Package | Description |
|---------|-------------|
| `Boutquin.OptionPricing.Calibration` | `ImpliedVolatilitySolver`, smile slice and surface builders, `QuoteArbitrageValidator`, Rubinstein `ImpliedBinomialTreeCalibrator`, Derman-Kani `ImpliedVolatilityTreeCalibrator`, calibration diagnostics |
| `Boutquin.OptionPricing.Exotics` | `BarrierTreePricer` (flat vol), `SmileAwareBarrierPricer` (surface), barrier parity, Broadie-Glasserman-Kou monitoring adjustments |

**Examples And Delivery**

The two executable examples and benchmark harness are verification applications. The seven reusable libraries, including `Examples.Shared`, are packed.

| Package | Description |
|---------|-------------|
| `Boutquin.OptionPricing.BlackScholesAndBeyond` | Book-aligned executable tracing all eleven chapter groups in *Black-Scholes and Beyond* (Chriss). CLI: `--chapter <n>` runs a single chapter; default runs all. |
| `Boutquin.OptionPricing.Examples` | Infrastructure-focused executable for ten examples covering bootstrapped curves, term-structure Greeks, multi-curve borrow cost, diagnostic gating, live market data pricing, discrete-dividend Merton pricing, tree-derived Greeks, tree dividend impact, and Heston stochastic-volatility Monte Carlo. CLI: `--example <id>` runs a single example. |
| `Boutquin.OptionPricing.Examples.Shared` | Shared class library consumed by both executables: `MarketHelpers` (flat and bootstrapped market construction) and `SimulationHelpers` (normal sampling for hedging simulation). |
| `Boutquin.OptionPricing.Benchmarks` | BenchmarkDotNet performance suite |

## Model Assumptions

### Black-Scholes

- lognormal underlying with constant volatility
- risk-free rate, dividend yield, and borrow cost derived from `OptionMarketData` curves
- cost-of-carry includes borrow rate: effective yield = q + b, so the forward is F = S × exp(−(q+b)T) / exp(−rT)
- European exercise only
- zero-time-to-expiry returns intrinsic value with a `Success` status

### CRR binomial tree

- recombining tree with flat volatility and flat discount rate
- optional flat continuous dividend yield
- American exercise evaluated at each node via early-exercise comparison
- converges to Black-Scholes as step count increases

### Merton escrowed-dividend model

- European exercise only (the closed form does not handle early exercise)
- present value of known cash dividends is removed from the spot ("escrowed") before option pricing: `S* = S₀ − PV(divs)`, `σ* = (S₀ / S*) · σ`
- volatility is uplifted to `σ*` strictly above `σ` whenever `PV(divs) > 0`; the uplift preserves instantaneous dollar volatility (`S*σ* = S₀σ`), not exact terminal stock-price variance
- discount factors `DF(tᵢ)` for each ex-dividend date `tᵢ` are read from `OptionMarketData`'s discount curve
- dividends with ex-dates on or after expiry are excluded from `PV(divs)`
- `S* > 0` is guarded; if dividends equal or exceed spot the pricer returns an `UnsupportedScenario` rather than a price

### CRR lattice Greeks (finite differences)

- Δ, Γ, and Θ are extracted directly from the valued binomial lattice via finite differences on the first two time steps (Chriss Display 7.2.1); no second tree valuation is required
- Vega and Rho are not lattice-extractable (they are derivatives with respect to parameters that change the tree geometry) and are returned as `null`
- Θ is reported per calendar day (divided by 365) to match the `BlackScholesGreeks.Theta` convention
- at least two steps are required; a two-step tree is the minimal lattice the differences can read
- accuracy converges asymptotically: within 1% of analytical Black-Scholes Greeks at ≥ 200 steps for European options

### Heston full-truncation Euler Monte Carlo

- variance follows a Cox-Ingersoll-Ross mean-reversion process correlated with the spot: `dv = κ(θ − v) dt + σ_v √v dW₂`
- full-truncation Euler discretization: `v⁺ = max(v, 0)` applied to the variance drift and diffusion at each step
- spot follows `S_{t+dt} = S_t · exp((r − q − b − ½v⁺) dt + √v⁺ √dt · Z₁)` over `m` steps per path
- correlated shocks use the identity `Z₂ = ρ Z₁ + √(1 − ρ²) Z⊥` where `Z₁` and `Z⊥` are independent standard normals; no Cholesky factorisation is needed for the 2×2 case
- the Feller condition (`2κθ ≥ σ_v²`) is not enforced — full-truncation Euler is well defined in the non-Feller regime; it is reported in diagnostics
- result price is the mean discounted terminal payoff across all paths; standard error and a 95% confidence interval are reported in diagnostics
- the pricer is single-threaded by contract; a fixed seed yields bit-identical reproducible prices; for parallel pricing, construct one pricer per thread with its own seeded source

### Implied binomial tree (Rubinstein)

- fits to a market volatility surface via Arrow-Debreu state prices
- recombining tree with state-dependent risk-neutral probabilities
- single flat discount curve for time value

### Implied volatility tree (Derman-Kani)

- fits to a market volatility surface via implied local volatilities
- forward induction to build tree; backward induction to price
- single flat discount curve for time value

## Validation Invariants

Tests enforce the following relationships across model families:

- put-call parity holds to within tolerance for all European pricers
- American put value is always ≥ European put value at the same inputs
- CRR tree price converges toward Black-Scholes as step count increases
- implied volatility round-trip: invert model price, re-price, recover original price to tolerance
- butterfly and calendar arbitrage are absent from valid smile surface inputs

## Quick Start

### Installation

Install the explicit version to keep the package family aligned; every dependency is a signed package on nuget.org (see [build verification](https://github.com/boutquin/Boutquin.OptionPricing/blob/v2.0.0/docs/runner-dependencies.md)).

```sh
dotnet add package Boutquin.OptionPricing.Domain --version 2.0.0
dotnet add package Boutquin.OptionPricing.Analytics --version 2.0.0
dotnet add package Boutquin.OptionPricing.Trees --version 2.0.0
```

### Price a European Call (flat-curve market)

`OptionMarketData` requires a discount curve. For exploration, wrap a constant rate in `FlatDiscountCurve` from `Boutquin.Curves.Core`:

```csharp
using Boutquin.Domain.Enumerations;
using Boutquin.Domain.Extensions;
using Boutquin.Curves.Abstractions.Identifiers;
using Boutquin.Curves.Core.Discounting;
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;

DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
var discountCurve = new FlatDiscountCurve(
    new CurveName("USD-OIS"), valuationDate, Currency.USD, continuouslyCompoundedZeroRate: 0.03);
var dividendCurve = new FlatDiscountCurve(
    new CurveName("DIVIDEND"), valuationDate, Currency.USD, continuouslyCompoundedZeroRate: 0.01);

var market = new OptionMarketData(valuationDate, spot: Currency.USD.Amount(100m), flatVolatility: 0.20, discountCurve, dividendCurve);
var option = new EuropeanOption(CallPut.Call, strike: 100m, Currency.USD, valuationDate.AddMonths(6));

var result = new BlackScholesPricer().Price(option, market);
Console.WriteLine($"Call price: {result.Price.Amount:F4}");
```

### Price with a Bootstrapped Curve (Fixture Data)

For deterministic testing, use `FixtureData.CreatePipeline()` — pre-built market data for USD, CAD, GBP, EUR with no network calls:

```csharp
using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.Domain.Enumerations;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.Curves.Recipes;
using Boutquin.Curves.Recipes.Testing;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAnalytics();                                    // registers CurveBuilder, calibrator, conventions
services.AddSingleton<IDataPipeline>(FixtureData.CreatePipeline());  // deterministic fixture data
services.AddLogging();
using var sp = services.BuildServiceProvider();

var builder = sp.GetRequiredService<CurveBuilder>();
var recipe = StandardCurveRecipes.UsdSofrDiscount();
var snapshot = await builder.BuildAsync(recipe, new DateOnly(2026, 4, 9)).ConfigureAwait(false);

// Verify bootstrap quality before pricing
double maxError = snapshot.Diagnostics.Repricing.Max(r => r.AbsoluteError);
if (!double.IsFinite(maxError) || maxError > 1e-6)
{
    throw new InvalidOperationException($"Curve failed quality gate: {maxError:E2}");
}

var market = OptionMarketData.FromCurveGroup(
    snapshot.CurveGroup, Currency.USD, spot: 100m, flatVolatility: 0.20, new BenchmarkName("SOFR"));
```

### Price with Real Market Data

For live data from public sources, replace `FixtureData.CreatePipeline()` with the real `Boutquin.MarketData` pipeline. Source adapters from [`Boutquin.MarketData.Adapter`](https://github.com/boutquin/Boutquin.MarketData.Adapter) fetch EOD data from free public APIs:

| Source | Data | Adapter Package |
|--------|------|-----------------|
| NY Fed | SOFR fixings | `MarketData.Adapter.NewYorkFed` |
| US Treasury | Par yields (2Y-30Y) | `MarketData.Adapter.UsTreasury` |
| Bank of Canada | CORRA fixings, zero curves | `MarketData.Adapter.BankOfCanada` |
| Bank of England | SONIA fixings | `MarketData.Adapter.BankOfEngland` |
| ECB | ESTR fixings | `MarketData.Adapter.Ecb` |
| CME | SOFR futures settlements | `MarketData.Adapter.Cme` |

```csharp
using Boutquin.Domain.Enumerations;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.Curves.Recipes;
using Boutquin.MarketData.Abstractions.Calendars;
using Boutquin.MarketData.Adapter.NewYorkFed;
using Boutquin.MarketData.Adapter.UsTreasury;
using Boutquin.MarketData.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();
services.AddAnalytics();                // CurveBuilder, calibrator, reference data
services.AddMarketDataKernel();         // IDataPipeline with transport, L1/L2 caching, normalization
services.AddMarketDataNewYorkFed();     // SOFR overnight fixings from NY Fed Markets API
services.AddMarketDataUsTreasury();     // Treasury par yields (2Y-30Y)
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

using var sp = services.BuildServiceProvider();
var builder = sp.GetRequiredService<CurveBuilder>();
var calendar = sp.GetRequiredService<IBusinessCalendar>();

// EOD data is published after market close — use previous business day
DateOnly valuationDate = calendar.Advance(DateOnly.FromDateTime(DateTime.Today), -1);
var snapshot = await builder.BuildAsync(StandardCurveRecipes.UsdSofrDiscount(), valuationDate).ConfigureAwait(false);

// Verify bootstrap quality before pricing
double maxError = snapshot.Diagnostics.Repricing.Max(r => r.AbsoluteError);
if (!double.IsFinite(maxError) || maxError > 1e-6)
{
    throw new InvalidOperationException($"Curve failed quality gate: {maxError:E2}");
}

var market = OptionMarketData.FromCurveGroup(
    snapshot.CurveGroup, Currency.USD, 100m, 0.20d, new BenchmarkName("SOFR"));
// Now price with the latest available real discount curve

// Provenance shows data source and actual business date
foreach (var prov in snapshot.Provenance.DistinctBy(p => p.Dataset))
{
    Console.WriteLine($"{prov.Dataset}: {prov.RetrievalMode} via {prov.Provider} [{prov.Freshness} {prov.DataDate}]");
}

// DATE_ROLLBACK warnings surface when data date != valuation date
foreach (var issue in snapshot.DataIssues.Where(i => i.Code.Value == "DATE_ROLLBACK"))
{
    Console.WriteLine($"[{issue.Severity}] {issue.Message}");
}
```

This is Example 06 in the Examples project. The data flow is: `StandardCurveRecipes` defines which data to fetch → `CurveBuilder` calls `IDataPipeline` → adapters fetch from NY Fed and Treasury APIs → rates are extracted and passed to the bootstrap calibrator → calibrated curves are returned in a `CurveSnapshot` with full provenance and data-quality issues.

Each `DataProvenance` record indicates whether data was retrieved via `RetrievalMode.Api` (live fetch), `RetrievalMode.Snapshot` (L2 disk cache), or `RetrievalMode.Cache` (L1 memory), along with the actual `DataDate` of the underlying data. When the data date differs from the requested valuation date (e.g., requesting Monday's data before EOD publication), adapter and node layers emit `DATE_ROLLBACK` warnings. Issues are persisted through the L2 cache so they are visible on cached replays. Consecutive runs hit the L2 disk cache and skip HTTP calls.

## Architecture

`Boutquin.OptionPricing` is organized in four dependency tiers:

- **Contracts** — `Abstractions`, `Domain`: stable contracts, option types, and result objects. The only tier downstream pricing consumers need to reference directly. Numerical primitives (root-finding, interpolation, distributions) are provided by [`Boutquin.Numerics`](https://github.com/boutquin/Boutquin.Numerics).
- **Vanilla Pricing Models** — `Analytics`, `Trees`: closed-form Black-Scholes, full Greeks, put-call parity, CRR/JR/flexible binomial trees, American exercise.
- **Smile, Calibration, And Exotics** — `Calibration`, `Exotics`: implied volatility inversion, smile surface construction, Rubinstein and Derman-Kani implied tree calibration, barrier pricing with flat-vol and smile-aware pricers.
- **Examples And Delivery**

The two executable examples and benchmark harness are verification applications. The seven reusable libraries, including `Examples.Shared`, are packed. — `BlackScholesAndBeyond`, `Examples`, `Examples.Shared`, `Benchmarks`: book-aligned chapter walkthroughs, infrastructure-focused examples, shared helpers, and performance benchmarks.

Dependencies flow in one direction: Exotics and Calibration depend on Trees and Analytics; all depend on Contracts and Numerics. `Boutquin.OptionPricing` depends on [`Boutquin.Curves`](https://github.com/boutquin/Boutquin.Curves) for curve abstractions — the dependency never flows in reverse.

See [docs/curve-layer.md](https://github.com/boutquin/Boutquin.OptionPricing/blob/v2.0.0/docs/curve-layer.md) for the Curves dependency design and [docs/repository-map.md](https://github.com/boutquin/Boutquin.OptionPricing/blob/v2.0.0/docs/repository-map.md) for per-package navigation guidance.

## Directory Structure

```
Boutquin.OptionPricing/
├── src/                                    # Source projects (9)
│   ├── Boutquin.OptionPricing.Abstractions/        # Core interfaces and payoff contracts
│   ├── Boutquin.OptionPricing.Domain/              # Option contracts, market data, result types
│   ├── Boutquin.OptionPricing.Analytics/           # Black-Scholes pricer, Greeks, hedging
│   ├── Boutquin.OptionPricing.Trees/               # Binomial tree pricing (CRR, JR, flexible)
│   ├── Boutquin.OptionPricing.Calibration/         # Implied vol, smile surface, implied trees
│   ├── Boutquin.OptionPricing.Exotics/             # Barrier and exotic options
│   ├── Boutquin.OptionPricing.BlackScholesAndBeyond/ # Book-aligned chapter examples
│   ├── Boutquin.OptionPricing.Examples/            # Infrastructure-focused examples
│   └── Boutquin.OptionPricing.Examples.Shared/    # Shared market construction helpers
├── tests/                                  # Test projects (10)
│   ├── Boutquin.OptionPricing.Domain.Tests/        # Domain contracts and market data
│   ├── Boutquin.OptionPricing.Analytics.Tests/     # Black-Scholes and Greeks
│   ├── Boutquin.OptionPricing.Trees.Tests/         # Binomial tree pricing
│   ├── Boutquin.OptionPricing.Calibration.Tests/   # Implied vol and calibration
│   ├── Boutquin.OptionPricing.Exotics.Tests/       # Barrier option tests
│   ├── Boutquin.OptionPricing.PropertyTests/       # Property-based tests (put-call parity, convergence)
│   ├── Boutquin.OptionPricing.IntegrationTests/    # End-to-end integration
│   ├── Boutquin.OptionPricing.Tests.Verification/  # Cross-language reference vectors
│   └── Boutquin.OptionPricing.ArchitectureTests/   # Dependency enforcement
├── benchmarks/
│   └── Boutquin.OptionPricing.Benchmarks/  # BenchmarkDotNet suite
├── docs/                                   # Architecture and usage documentation
└── .github/                                # CI/CD workflows
```

## Documentation

| Document | Description |
|----------|-------------|
| [docs/repository-map.md](https://github.com/boutquin/Boutquin.OptionPricing/blob/v2.0.0/docs/repository-map.md) | Per-package descriptions with external dependency and test structure |
| [docs/curve-layer.md](https://github.com/boutquin/Boutquin.OptionPricing/blob/v2.0.0/docs/curve-layer.md) | Market curve integration design: why `OptionMarketData` uses curve objects |

## Contributing

Contributions are welcome! Please read the contributing guidelines first.

### Reporting Bugs

If you find a bug, please report it by opening an issue on the [Issues](https://github.com/boutquin/Boutquin.OptionPricing/issues) page with:

- A clear and descriptive title
- Steps to reproduce the issue
- Expected and actual behavior
- Screenshots or code snippets, if applicable

### Contributing Code

1. Fork the repository and clone locally
2. Create a feature branch: `git checkout -b feature-name`
3. Make your changes following the style guides
4. Commit with clear messages: `git commit -m "Add feature X"`
5. Push and open a pull request

## Disclaimer

Boutquin.OptionPricing is open-source software provided under the Apache 2.0 License. It is a general-purpose library intended for educational and research purposes.

**This software does not constitute financial advice.** The option pricing, volatility, and risk analysis tools are provided as-is for research and development. Before using any financial calculations in production, consult with qualified professionals who understand your specific requirements and regulatory obligations.

## License

This project is licensed under the Apache 2.0 License -- see the [LICENSE](https://github.com/boutquin/Boutquin.OptionPricing/blob/v2.0.0/LICENSE.txt) file for details.

## Contact

For inquiries, please open an issue or reach out via [GitHub Discussions](https://github.com/boutquin/Boutquin.OptionPricing/discussions).
