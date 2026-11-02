# Curve Layer

## Purpose

The curve layer separates market infrastructure from option-model logic. OptionPricing
consumes curves, day-count conventions, and interpolation primitives from the
Boutquin.Curves, Boutquin.Numerics, and Boutquin.MarketData stacks rather than
re-implementing them.

## Packages

These are consumed as external NuGet packages (not embedded under OptionPricing `src/`):

- `Boutquin.Curves.Abstractions` — curve interfaces (`IDiscountCurve`, `IForwardCurve`,
  `ICurveGroup`), `MarketEnvironment`, compounding and curve-reference contracts.
- `Boutquin.MarketData.Abstractions` / `Boutquin.MarketData.Storage` — `IFixingsStore`
  (keyed by `BenchmarkName`) and its `InMemoryFixingsStore` implementation.
- `Boutquin.Curves.Core` — flat and interpolated discount/forward curves
  (`FlatDiscountCurve`, `InterpolatedDiscountCurve`, `FlatForwardCurve`), `CurveGroup`.
- `Boutquin.Curves.Bootstrap` — piecewise bootstrap engine for OIS, deposit, futures,
  FRA, and swap-driven curve construction.
- `Boutquin.Curves.Interpolation` — `INodalCurveInterpolator` and concrete
  implementations (LogLinearDiscountFactor, LinearZeroRate, FlatForward, MonotoneCubic,
  MonotoneConvex) used inside `InterpolatedDiscountCurve`.
- `Boutquin.Curves.Recipes` — pre-built curve recipes (USD SOFR, CAD CORRA) and
  `CurveBuilder` for one-call bootstrapped curve construction.
- `Boutquin.MarketData.DayCount` — `IYearFractionCalculator` and standard day-count
  singletons (ACT/360, ACT/365F, 30/360).
- `Boutquin.Numerics` — math primitives (normal CDF/PDF/inverse, root solvers,
  interpolators, Monte Carlo, statistics) consumed by the option pricing stack.

## Design choice

`OptionMarketData` does not store scalar rates. The pricing stack derives time, discount
factors, dividend yields, borrow rates, and forward prices from:

- valuation date
- discount curve (`IDiscountCurve`)
- optional dividend curve (`IDiscountCurve`)
- optional borrow curve (`IDiscountCurve`)
- optional projection curve (`IForwardCurve`)
- optional fixings store (`IFixingsStore`) for path-dependent pricing

This keeps curves as the market source of truth and eliminates a second, competing
representation of rates inside the option domain.

## Market construction patterns

### Direct constructor (Pattern A — flat curves for testing)

Pass individual `IDiscountCurve` instances directly to the `OptionMarketData`
constructor. Best for unit tests and exploration with flat-rate curves.

### FromCurveGroup (Pattern C — recommended for production)

`OptionMarketData.FromCurveGroup(curveGroup, currency, spot, vol)` resolves discount,
dividend, borrow, and forward curves automatically by `CurveRole`. Works directly with
`CurveBuilder` output:

```csharp
var recipe = StandardCurveRecipes.UsdSofrDiscount();
var snapshot = await builder.BuildAsync(recipe, valuationDate);

var market = OptionMarketData.FromCurveGroup(
    snapshot.CurveGroup, new CurrencyCode("USD"), 100m, 0.20);
```

### FromMarketEnvironment (Pattern D — full environment with fixings)

`OptionMarketData.FromMarketEnvironment(env, currency, spot, vol)` resolves curves and
maps `env.Fixings` into an `IFixingsStore`. Use this when path-dependent pricers need
historical benchmark rates.

### Diagnostic gating

Always verify bootstrap quality before pricing:

```csharp
double maxError = snapshot.Diagnostics.Repricing.Max(r => r.AbsoluteError);
if (maxError > 1e-6)
    throw new InvalidOperationException($"Curve failed quality gate: {maxError:E2}");
```

## Time-axis consistency (Phase 5c)

Every `T` consumed by the pricing stack flows through the discount curve's
`IYearFractionCalculator`, never through ad-hoc day-count arithmetic. The helpers:

- `OptionMarketData.TimeToExpiryInYears(expiryDate)` — forward direction, year fraction
  under the discount curve's day-count.
- `OptionMarketData.InvertYearFraction(maturityInYears)` — inverse, returns the
  `DateOnly` whose year fraction from the valuation date matches a target (bisection over
  calendar days, one-day rounding resolution).

Every site that used to derive a date from years by `(int)(years * 365.25)` now routes
through `InvertYearFraction`, so the calibrators, hedge simulator, and vol surface all
share the same time axis as the discount stack.

## Vol-surface reads via the Curves day-count contract

`VolatilitySurface.GetVol` accepts two overloads:

- `GetVol(decimal strike, double maturityInYears)` — raw year-fraction query; the
  strike axis uses `MonotoneCubicInterpolator` (Numerics), the maturity axis uses
  `CubicSplineInterpolator` with `ModifiedNotAKnot` / `Natural` / two-point-linear
  depending on slice count.
- `GetVol(decimal strike, DateOnly valuationDate, DateOnly expiryDate, IYearFractionCalculator dayCount)`
  — dated query; derives the year fraction from the supplied day-count so surface reads
  share the exact time axis used by the discount stack. Preferred when callers already
  hold dated contract state.

Both overloads return identical values when the year fractions agree. The dated overload
prevents silent divergence between the surface's time axis and the curve's.

## TreeModelInput convenience factory

`TreeModelInput.FromMarketData(market, strike, expiryDate, steps)` extracts curves,
spot, and volatility from an `OptionMarketData` instance, eliminating manual parameter
duplication when building binomial trees.

## Settlement calendars

Option contracts accept optional settlement parameters from
`Boutquin.Curves.Abstractions` / `Boutquin.MarketData.Calendars`:

- `IBusinessCalendar` — adjusts expiry dates that fall on non-business days.
- `BusinessDayAdjustment` — rolling rule (Following, ModifiedFollowing, Preceding).
- `settlementLagDays` — business-day lag from adjusted expiry to cash settlement.

The computed `SettlementDate` property resolves these. When no calendar is provided,
`SettlementDate` equals `ExpiryDate` — backward-compatible with existing code.
