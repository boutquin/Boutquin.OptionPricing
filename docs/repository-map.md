# Repository Map

## External Dependencies

Market infrastructure is consumed via NuGet package references:

| Package | What OptionPricing uses it for |
|---------|-------------------------------|
| `Boutquin.Curves.Abstractions` | `IDiscountCurve`, `IForwardCurve`, `CurveName`, `CurveRole`, day-count contracts |
| `Boutquin.Curves.Core` | `FlatDiscountCurve` for quick exploration and tests |
| `Boutquin.Curves.Bootstrap` | Production bootstrap engine (consumed indirectly via Recipes) |
| `Boutquin.Curves.Recipes` | `StandardCurveRecipes`, `CurveBuilder`, `FixtureData` for examples and integration tests |
| `Boutquin.MarketData.Abstractions` | `IDataPipeline`, `CurrencyCode`, `DataProvenance`, `DataIssue`, `IBusinessCalendar` |
| `Boutquin.MarketData.DependencyInjection` | `AddMarketDataKernel` wires transport, L1/L2 cache, and normalization |

## Source Packages

**Contracts**

Numerical primitives (root-finding, interpolation, distributions) are provided by `Boutquin.Numerics`.

| Package | What lives here | When to work here |
|---------|----------------|-------------------|
| `Boutquin.OptionPricing.Abstractions` | `IOptionPricer`, `IGreekProvider`, `ICalibrator`, `IContextualOptionPricer`, `IDiagnosticSource`, `ICalculationResult` | Adding a new pricing interface; extending the calibrator contract |
| `Boutquin.OptionPricing.Domain` | `OptionContract`, `EuropeanOption`, `AmericanOption`, `BarrierOption`, `OptionMarketData`, enums (`CallPut`, `ExerciseStyle`, `BarrierKnockType`), `PriceResult`, `GreekSet` | Changing option domain types; adding a new instrument form |

**Vanilla Pricing Models**

| Package | What lives here | When to work here |
|---------|----------------|-------------------|
| `Boutquin.OptionPricing.Analytics` | `BlackScholesPricer`, `BlackScholesGreeks`, `BlackScholesDividendPricer`, `DeltaHedgeSimulator`, `PutCallParity`, `RiskNeutralValuation` | Changing Black-Scholes formula wiring; adding a new closed-form model |
| `Boutquin.OptionPricing.Trees` | `CoxRossRubinsteinTreeBuilder`, `JarrowRuddTreeBuilder`, `FlexibleBinomialTreeBuilder`, `EuropeanTreePricer`, `AmericanTreePricer`, `ArrowDebreuStatePriceCalculator` | Adding a new tree builder; adjusting American exercise logic |

**Smile, Calibration, And Exotics**

| Package | What lives here | When to work here |
|---------|----------------|-------------------|
| `Boutquin.OptionPricing.Calibration` | `ImpliedVolatilitySolver`, `SmileSliceBuilder`, `VolatilitySurfaceBuilder`, `QuoteArbitrageValidator`, `ImpliedBinomialTreeCalibrator` (Rubinstein), `ImpliedVolatilityTreeCalibrator` (Derman-Kani), `CalibrationReport` | Extending smile fitting; adding a calibration diagnostic |
| `Boutquin.OptionPricing.Exotics` | `BarrierTreePricer`, `SmileAwareBarrierPricer`, `BarrierMonitoringAdjustments` (Broadie-Glasserman-Kou) | Adding a new barrier type; integrating monitoring adjustments |

**Examples And Delivery**

| Package | What lives here |
|---------|----------------|
| `Boutquin.OptionPricing.BlackScholesAndBeyond` | Book-aligned executable: 11 chapter groups tracing *Black-Scholes and Beyond* (Chriss); CLI `--chapter <n>` |
| `Boutquin.OptionPricing.Examples` | Six infrastructure-focused examples: bootstrapped curves, term-structure Greeks, `FromCurveGroup` factory, multi-curve borrow cost, diagnostic gating, live market data; CLI `--example <id>` |
| `Boutquin.OptionPricing.Examples.Shared` | Shared helpers (`MarketHelpers`, `SimulationHelpers`) consumed by both executables |
| `Boutquin.OptionPricing.Benchmarks` | BenchmarkDotNet suite for core pricing paths |

## Tests

| Project | Coverage |
|---------|---------|
| `Domain.Tests` | Option contract invariants, market-data construction, `FromCurveGroup`/`FromMarketEnvironment` factories |
| `Analytics.Tests` | Black-Scholes pricing, Greeks, put-call parity, dividend impact |
| `Trees.Tests` | CRR construction, American exercise, CRR convergence to Black-Scholes |
| `Calibration.Tests` | Implied vol round-trip, smile construction, arbitrage checks, implied tree calibration |
| `Exotics.Tests` | Barrier pricing, in-out parity, monitoring adjustments |
| `PropertyTests` | Broad invariant checks (FsCheck): put-call parity, monotonicity, CRR convergence |
| `Tests.Verification` | Cross-language reference vectors against Python/QuantLib ground truth |
| `IntegrationTests` | End-to-end workflows: bootstrapped curves, multi-curve market data, diagnostic gating |
| `ArchitectureTests` | Dependency-tier enforcement across all src packages |
