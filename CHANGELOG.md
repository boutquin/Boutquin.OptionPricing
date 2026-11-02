# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [2.0.0] — 2026-11-02

The first stable 2.0 release. The 2.0.0-local-20260622 entry below was an unpublished local build; its changes ship here.

### Fixed

- Curve-based put/call parity consumes both dividend and borrow discount factors. Node-dependent lattice probabilities are honored in Arrow-Debreu propagation and pricing.
- Nonfinite inputs, invalid contract flags, malformed lattices, caller-mutated collections, and nonrepresentable calculations fail explicitly. Calibrated trees retain their calibration discount rate.
- Implied-tree wing fallbacks preserve ordered nodes and the forward bracket; invalid transitions cannot silently become valid through probability clipping.
- Delta-hedge simulations reinvest the model-equivalent continuous yield `q+b` between rebalances. This models the pricing convention, not actual stock-loan fees or collateral. Invalid hedge paths and nontrivial paths for expired options are rejected.
- README examples compile against the current APIs, unsuccessful book-demo prices are reported as failures, and benchmark CLI filters/jobs are honored.

- Bootstrapped fixture pricing is regression-tested through the shared example market helper and the same `1e-6` repricing gate as example 01. The integration suite no longer accepts the historical sparse-coupon bootstrap residual at the looser `1e-4` threshold.

### Changed

- **Exact, signed dependencies from nuget.org alone:** Numerics 2.0.0, Curves 2.0.0, MarketData 2.0.1, MarketData.Adapter 2.0.1, Storage 2.0.1 and Domain 1.0.1 are pinned by exact version in `Directory.Packages.props`. `dependency-source.json` records each archive's hash, signature-excluded content hash, original payload and source revision, and `scripts/check-runner-dependencies.py` verifies them against a cold restore. NuGet configurations name only nuget.org. Seven reusable libraries ship with assembly version 2.0.0.0. Validation includes fresh pinned QuantLib engines, regression and injected-fault controls, package-only consumption, actual DLL/PDB inspection and Linux/macOS/Windows jobs.
- **Immutable release publishing:** packages are built once from the public source commit, frozen in a SHA-256 manifest, attached to the GitHub release and uploaded by `publish.yml` through nuget.org trusted publishing; no long-lived API key is stored.

#### Fixings store moved to MarketData (breaking)

- **`OptionMarketData.FixingsStore` is now `Boutquin.MarketData.Abstractions.IFixingsStore`.** Curves 1.0 removed its `Boutquin.Curves.Abstractions.Fixings` shims, so fixings are keyed by the typed `BenchmarkName` instead of a string (`TryGetFixing(new BenchmarkName("USD-SOFR"), date, out value)`). `FromMarketEnvironment` seeds a `Boutquin.MarketData.Storage.InMemory.InMemoryFixingsStore` from `env.Fixings`, wrapping each key as a `BenchmarkName`. Callers that built their own store construct it from `(BenchmarkName, DateOnly, decimal)` tuples; the MarketData store has no `AddFixing`. `Boutquin.OptionPricing.Domain` now references `Boutquin.MarketData.Storage`.

#### Contract-currency `Money` adoption (breaking)

- **`OptionContract` now carries a `Currency`, and `Strike`, `OptionMarketData.Spot`, and `PriceResult.Price` are `Money`.** Each option contract (`EuropeanOption`, `AmericanOption`, `BarrierOption`) gains a required `Currency` constructor parameter (immediately after the strike); the strike is exposed as a `Money` denominated in that currency. The market snapshot's spot price and the priced premium are likewise `Money`. Construction rejects `Currency.Unspecified`, and a strike whose currency differs from the contract currency.
- **Same-currency invariant is now enforced.** Every public pricer entry point (`BlackScholesPricer`, `MertonDiscreteDividendPricer`, `SimpleHestonMcPricer`, `EuropeanTreePricer`, `AmericanTreePricer`, `BarrierTreePricer`, `SmileAwareBarrierPricer`) requires `market.Spot.Currency == contract.Currency` and throws `Boutquin.Domain.CurrencyMismatchException` on a mismatch instead of silently computing a meaningless number across currencies. `IntrinsicValue(Money)` enforces the same invariant. The new exception surface is documented on every member that can raise it.
- **`PriceResult.Unsupported` gains a required `Currency` parameter.** An unsupported-scenario result now returns a zero amount in the contract currency (never `Currency.Unspecified`), so a downstream consumer can sum the unsupported-scenario zero into a same-currency option book without a `CurrencyMismatchException`.
- **The pricing engine stays `double`.** `Money` is unwrapped to `decimal`/`double` at each entry point and the premium is wrapped back as `Money` only at the result boundary — the numerical results are byte-identical to the pre-`Money` baseline (pinned by golden-vector tests). Greeks (`GreekSet`), volatilities, discount factors, rates, and year fractions remain `double`. `BarrierOption.BarrierLevel` remains `decimal`.

### Added

#### Discrete-dividend European pricing (Merton escrowed-dividend model)

- `MertonDiscreteDividendPricer` — European option pricer using Merton's escrowed-dividend approximation. Accepts a `DividendSchedule` of known cash dividends; computes the present value of dividends paid before expiry using the market's discount curve, subtracts it from the spot (`S* = S₀ − PV(divs)`), and uplifts the volatility (`σ* = (S₀/S*) · σ`) before delegating to `BlackScholesPricer`. Result diagnostics carry `pvDividends`, `escrowedSpot`, `upliftedVolatility`, and the model name `"Merton-DiscreteDividend"`. Edge cases: empty schedule returns a result equal to `BlackScholesPricer` to 1e-12; dividends exceeding spot return `UnsupportedScenario` rather than throwing.
- Example 21 — prices an ATM one-year call with two $0.80 cash dividends and compares the Merton escrowed price to the no-dividend Black-Scholes price, showing the dividend impact and the escrowed intermediates.

#### Lattice-derived hedge parameters (Δ, Γ, Θ from the tree)

- `TreeGreeksCalculator` — computes Delta, Gamma, and Theta directly from a valued binomial lattice via finite differences on the first two time steps (Chriss Display 7.2.1), without a second tree valuation or bump-and-reprice. Theta is scaled per calendar day to match the `BlackScholesGreeks` convention. Vega and Rho are returned as `null` (they require repricing against a perturbed tree).
- `LatticeValuationResult` — carries the full option-value and spot lattices produced by `EuropeanTreePricer.PriceWithLattice` and `AmericanTreePricer.PriceWithLattice`, alongside the root price.
- `PriceWithLattice` overloads on `EuropeanTreePricer` and `AmericanTreePricer` — return the full valued lattice for downstream Greek extraction, in addition to the root price.
- Example 22 — builds a 400-step CRR tree, reads the European-call Greeks off the lattice alongside analytical Black-Scholes Greeks, and shows American-put lattice Greeks with their sign/range invariants.

#### Tree dividend impact walkthrough

- Example 23 — prices a six-month ATM European call and a deep-ITM American call on a CRR tree with and without a 3% continuous dividend yield, then demonstrates the early-exercise-boundary shift: the American call boundary is empty at `q = 0%` and non-empty (200 active steps) at `q = 8%`.

#### Minimal stochastic-volatility Heston Monte Carlo

- `HestonParameters` — immutable record of the five Heston model parameters (`V0`, `Kappa`, `Theta`, `SigmaV`, `Rho`), placed in the domain layer so future advanced Heston pricers can share it without an Analytics dependency.
- `SimpleHestonMcPricer` — European option pricer using full-truncation Euler discretization of the Heston (1993) stochastic-volatility model. Simulates spot and variance paths jointly, correlating the variance shock via `Z₂ = ρ Z₁ + √(1 − ρ²) Z⊥`. Result diagnostics carry `standardError`, `confidenceInterval95`, `paths`, `steps`, and `fellerSatisfied`. Fixed-seed injection yields bit-identical reproducible prices. Reproduces Black-Scholes in the zero-vol-of-vol limit and produces a negatively-skewed implied-volatility smile for negative spot/variance correlation.
- Example 24 — verifies the zero-vol-of-vol Black-Scholes limit and prices across five strikes at `ρ = −0.80`, showing the downward-sloping implied-volatility smile that flat-vol Black-Scholes cannot produce.

## [2.0.0-local-20260622] — not published; shipped as 2.0.0

### Changed

- **Currency enum unified onto `Boutquin.Domain.Currency` (breaking).** The currency parameter of `OptionMarketData.FromCurveGroup` and `OptionMarketData.FromMarketEnvironment` — and the internal curve-lookup helper — changes type from `Boutquin.MarketData.Abstractions.ReferenceData.CurrencyCode` to `Boutquin.Domain.Currency` (the full ISO 4217 enumeration, 177 members, `Unspecified = 0`). Both enumerations share identical ISO 4217 numeric backing (USD = 840, EUR = 978, …) and the former 13-member set is a same-name, same-value subset of `Currency`, so curve-lookup keys and all numeric values are preserved — callers update only the literal (`CurrencyCode.USD` → `Currency.USD`). This is a pure type unification; the pricing engine, `OptionContract.Strike`, `OptionMarketData.Spot`, and `PriceResult.Price` are unchanged (no `Money` adoption in this release).
- **Explicit `Boutquin.Domain` dependency.** `Boutquin.OptionPricing.Domain` now carries a direct `Boutquin.Domain` package reference (previously the currency enum came transitively from the MarketData kernel).
- **Upstream package pins.** `Boutquin.Domain` → `0.7.1-local-20260622`; `Boutquin.Curves.*` → `2.0.0-local-20260622`; `Boutquin.MarketData.*` → `1.1.0-local-20260622`.

## [1.0.0] - 2026-04-17

### Added

#### Executables and shared helpers

- **`Boutquin.OptionPricing.BlackScholesAndBeyond`** — book-aligned executable tracing all eleven chapter groups in *Black-Scholes and Beyond* (Chriss). CLI: `--chapter <n>` runs a single chapter; default runs all. Chapters 1–3 cover security payoffs and arbitrage, normal-distribution properties and N(x) approximations (all four from Display 2.5.1/2.5.2 vs. Laikov 2025), and GBM price distribution with volatility calibration. Chapters 4–11 cover Black-Scholes pricing and Greeks, hedging, Arrow-Debreu state prices, trees, implied volatility, implied trees, and barrier options — all with *Black-Scholes and Beyond* section references and consistent parameters (S=100, K=100, T=1 yr, r=5%, σ=20%).
- **`Boutquin.OptionPricing.Examples.Shared`** — class library shared by both executables: `MarketHelpers` (`CreateFlatMarket`, `BuildUsdSnapshotAsync`, `CreateBootstrappedMarketAsync`, `CreateReferenceData`) and `SimulationHelpers` (`NormalSample`).

#### Market integration

- `OptionMarketData.FromCurveGroup()` — static factory resolving discount, dividend, borrow, and forward curves from an `ICurveGroup` by `CurveRole` and currency.
- `OptionMarketData.FromMarketEnvironment()` — static factory resolving curves and mapping environment fixings into an `IFixingsStore`.
- `IFixingsStore? FixingsStore` property on `OptionMarketData` for path-dependent pricing models.
- `SettlementDate` computed property on `OptionContract` with optional `IBusinessCalendar`, `BusinessDayAdjustment`, and settlement lag support — passed through all option subtypes.
- `TreeModelInput.FromMarketData()` — static factory extracting curves, spot, and volatility from `OptionMarketData`.

#### Calibration and exotics (Phase 3)

- `IOptionPriceProvider` interface and `SurfaceDerivedPriceProvider` for extensible calibration price sources.
- `ImpliedTreeResult` record carrying calibrated tree, local volatilities, and Arrow-Debreu state prices.
- `ImpliedBinomialTreeCalibrator` — Rubinstein-style forward induction recovering implied transition probabilities and local volatilities from the volatility surface.
- `ImpliedVolatilityTreeCalibrator` — Derman-Kani-style forward construction with Arrow-Debreu price induction and local volatility extraction.
- `BarrierTreePricer` — backward induction on CRR tree with barrier monitoring at each node; knock-in derived via barrier parity.
- `SmileAwareBarrierPricer` — barrier pricing on calibrated implied trees for smile-consistent dynamics.
- `BarrierMonitoringAdjustments.AdjustedBarrierLevel()` — Broadie-Glasserman-Kou discrete-monitoring correction formula.

#### Test coverage

- 25 tests across calibration, exotics, and barrier pricing.
- `MarketIntegrationExampleTests` — 4 integration tests covering infrastructure examples.
- `OptionMarketDataFactoryTests` — 9 tests for `FromCurveGroup` and `FromMarketEnvironment` factories and fixings.
- `OptionContractSettlementTests` — 7 tests for calendar-aware settlement.
- `TreeModelInputFactoryTests` — 4 tests for `FromMarketData` factory.

### Changed

#### Examples restructure

- **`Boutquin.OptionPricing.Examples`** trimmed to six infrastructure-only examples (bootstrapped curve pricing, term-structure Greeks, `FromCurveGroup` factory, multi-curve borrow cost, diagnostic-gated pricing, and live market data). CLI changed from `--chapter <n>` to `--example <id>`.
- **`Boutquin.OptionPricing.slnx`** — XML solution file replacing the legacy `Boutquin.OptionPricing.sln`. Includes all 9 src projects, 10 test projects, and 1 benchmark project.

#### Dependency upgrades (Curves 0.16.0 / MarketData 0.16.0 / Numerics 1.0.0)

- `Boutquin.Numerics` bumped from 0.6.0 → **1.0.0**.
- `Boutquin.Curves.*` bumped from 0.15.0 → **0.16.0** (Abstractions, Bootstrap, Conventions, Core, Interpolation, Recipes).
- `Boutquin.MarketData.*` bumped from 0.15.0 → **0.16.0** (Abstractions, Calendars, Conventions, DependencyInjection, plus new `Boutquin.MarketData.Adapter.Shared`).
- `Boutquin.MarketData.Adapter.NewYorkFed` / `UsTreasury` bumped from 0.15.0 → **0.16.0**.
- `Boutquin.Storage` added at **1.1.1**.
- **Breaking-change migrations applied across all call sites:**
  - `CurrencyCode` migrated from struct (`new CurrencyCode("USD")`) to enum (`CurrencyCode.USD`) — 24 files updated; 5 `static readonly` fields promoted to `const`.
  - `BenchmarkKind`, `RateBenchmark`, `BenchmarkName` namespace moved from `Boutquin.Curves.Abstractions.Benchmarks` to `Boutquin.MarketData.Abstractions.ReferenceData`.
  - `InstrumentConventionRegistry` moved from `Boutquin.Curves.Conventions` to `Boutquin.MarketData.Conventions`.
  - `DataProvenance.RetrievalMode` migrated from `string` to `RetrievalMode` enum (`Api`, `Cache`, `Snapshot`).
  - `DataIssue.Code` migrated from `string` to `IssueCode` record struct; comparisons updated to use `.Value`.

#### Best-in-class Numerics adoption (Phase 5b)

- **BrentSolver** replaces BisectionSolver for implied volatility. `ImpliedVolatilitySolver` now uses `Boutquin.Numerics.Solvers.BrentSolver` — worst-case convergence in `⌈log₂((b−a)/tol)⌉` iterations while typically converging super-linearly on the smooth Black-Scholes IV objective (Oliveira-Della-Pasqua-Steffen 2024 halving-interval guarantee).
- **`CumulativeNormal.Evaluate`** (Laikov 2025, arXiv:2504.05068) replaces `NormalDistribution.Cdf` at all pricing call sites — exponential-free, branchless, ~14-digit precision.
- **`MonotoneCubicInterpolator`** (Fritsch-Carlson + arXiv:2402.01324 endpoint fix) replaces two-point linear interpolation on the volatility-smile strike axis, eliminating arbitrage-producing oscillation at off-grid strikes.
- **Adaptive `CubicSplineInterpolator`** replaces two-point linear interpolation on the volatility surface maturity axis: `ModifiedNotAKnot` (Jarre 2025, arXiv:2507.05083) for ≥4 slices, `Natural` for 3 slices, two-point linear fallback for 2 slices (bit-identical to prior behavior).

#### Numerics and Curves migration (Phase 5a)

- Migrated off `Boutquin.Analytics.*` packages to `Boutquin.Numerics` + `Boutquin.Curves.*` — pure rename and package-reference migration across all src, tests, and benchmarks csproj files with no behavior change.
- `LinearInterpolator.Interpolate(x0, y0, x1, y1, x)` renamed to `TwoPointLinearInterpolator.Interpolate(...)` in `VolatilitySurface.cs`.

#### Example and test helpers

- Examples 11–14 added (term-structure Greeks, `FromCurveGroup` factory, multi-curve borrow cost, diagnostic-gated pricing).
- Examples 03, 04, 09, 10 migrated to `TreeModelInput.FromMarketData()`.
- Examples 06, 11 migrated to `OptionMarketData.FromCurveGroup()` with diagnostic gating.
- `CreateBootstrappedMarketAsync` helper migrated to `FromCurveGroup` with repricing diagnostics.
- Test helpers across `AmericanTreePricerTests`, `JarrowRuddTreeBuilderTests`, `TreeConvergenceTests`, `BarrierTreePricerTests`, and `SmileAwareBarrierPricerTests` migrated to `TreeModelInput.FromMarketData()`.

### Removed

- `Boutquin.OptionPricing.Numerics` package — numerical primitives (root-finding, interpolation, distributions) are now provided by `Boutquin.Numerics`.
- `Boutquin.OptionPricing.sln` — replaced by `Boutquin.OptionPricing.slnx`.
- Local `NormalCdf` helper (A&S 7.1.26, ~7 digits) — last call site migrated to `CumulativeNormal.Evaluate`.

## [0.1.0]

- Initial repository skeleton: source package boundaries, test projects, docs, CI workflows, and examples.
