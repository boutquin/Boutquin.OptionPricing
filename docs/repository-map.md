# Repository Map

## External Dependencies

Market infrastructure is consumed via NuGet package references:

| Package | What OptionPricing uses it for |
|---------|-------------------------------|
| `Boutquin.Analytics.Abstractions` | `IDiscountCurve`, `CurrencyCode`, `CurveName`, day-count contracts |
| `Boutquin.Analytics.Curves` | `FlatDiscountCurve` for quick exploration and tests |
| `Boutquin.Analytics.Curves.Bootstrap` | Production bootstrap engine (consumed indirectly via Recipes) |
| `Boutquin.Analytics.Recipes` | `StandardCurveRecipes` and `CurveBuilder` for examples and integration tests |

## Source Packages

**Contracts And Numerics**

| Package | What lives here | When to work here |
|---------|----------------|-------------------|
| `Boutquin.OptionPricing.Abstractions` | `IOptionPricer`, `IGreekProvider`, `ICalibrator`, `IContextualOptionPricer`, `IDiagnosticSource`, `IRootSolver`, `ICalculationResult` | Adding a new pricing interface; extending the calibrator contract |
| `Boutquin.OptionPricing.Domain` | `OptionContract`, `EuropeanOption`, `AmericanOption`, `BarrierOption`, `OptionMarketData`, enums (`CallPut`, `ExerciseStyle`, `BarrierKnockType`), `PriceResult`, `GreekSet` | Changing option domain types; adding a new instrument form |
| `Boutquin.OptionPricing.Numerics` | `NormalDistribution`, `CumulativeNormal`, solvers (`BisectionSolver`, `NewtonRaphsonSolver`, `SecantSolver`), `LinearInterpolator`, `CubicSplineInterpolator` | Adding a numerical algorithm; adjusting solver convergence tolerances |

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
| `Boutquin.OptionPricing.Examples` | Ten console examples (01 vanilla BS → 10 smile-aware barrier); bootstrapped-curve example (06) |
| `Boutquin.OptionPricing.Benchmarks` | BenchmarkDotNet suite for core pricing paths |

## Tests

| Project | Coverage |
|---------|---------|
| `Domain.Tests` | Option contract invariants, market-data construction |
| `Analytics.Tests` | Black-Scholes pricing, Greeks, parity |
| `Trees.Tests` | CRR construction, American exercise, convergence |
| `Numerics.Tests` | Solver correctness, interpolation accuracy |
| `Calibration.Tests` | Implied vol round-trip, smile construction, arbitrage checks, implied tree calibration |
| `Exotics.Tests` | Barrier pricing, parity, monitoring adjustments |
| `PropertyTests` | Broad invariant checks (FsCheck): put-call parity, monotonicity, CRR convergence |
| `IntegrationTests` | End-to-end workflows with bootstrapped curves |
