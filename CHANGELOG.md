# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

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
