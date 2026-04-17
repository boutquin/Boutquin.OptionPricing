# Boutquin.OptionPricing.Examples

Infrastructure-focused executable demonstrating how `Boutquin.OptionPricing` integrates with `Boutquin.Curves` and `Boutquin.MarketData` for bootstrapped-curve pricing, diagnostic gating, and live market data fetching.

These examples address the infrastructure layer of option pricing — curve construction, data provenance, diagnostic gating, and multi-curve market data — rather than the option models themselves. For book-aligned pricing examples that follow the conceptual progression in *Black-Scholes and Beyond* (Chriss), see `Boutquin.OptionPricing.BlackScholesAndBeyond`.

## Running

```bash
# Run all examples
dotnet run --project src/Boutquin.OptionPricing.Examples

# Run a specific example
dotnet run --project src/Boutquin.OptionPricing.Examples -- --example 01

# List all examples
dotnet run --project src/Boutquin.OptionPricing.Examples -- --help
```

## Examples

| ID | Title | What it demonstrates |
|----|-------|----------------------|
| 01 | Bootstrapped curve pricing | `CurveBuilder` + `FixtureData.CreatePipeline()` for deterministic USD SOFR discount; flat-curve vs. bootstrapped-curve price comparison; repricing quality gate. |
| 02 | Term-structure Greeks | Rho computed against a flat rate vs. a real term structure; how a non-flat discount curve changes the rate sensitivity. |
| 03 | `FromCurveGroup` factory pricing | `OptionMarketData.FromCurveGroup()` resolving discount and dividend curves from a `CurveSnapshot` by `CurveRole`; eliminating manual curve extraction. |
| 04 | Multi-curve with borrow cost | Separate discount, dividend, and borrow curves; forward-price impact of a securities-lending borrow cost alongside dividend yield. |
| 05 | Diagnostic-gated pricing | Bootstrap repricing diagnostics used as a quality gate before pricing; how `CurveSnapshot.Diagnostics` surfaces calibration accuracy. |
| 06 | Real market data pricing (live APIs) | Live pricing against NY Fed SOFR fixings and US Treasury par yields via `Boutquin.MarketData.Adapter`; data provenance (`RetrievalMode`, `DataDate`); `DATE_ROLLBACK` data-issue handling; L2 disk cache on repeat runs. |

## Dependencies

| Package | Purpose |
|---------|---------|
| `Boutquin.OptionPricing.Analytics` | `BlackScholesPricer`, `BlackScholesGreeks` |
| `Boutquin.OptionPricing.Domain` | `EuropeanOption`, `OptionMarketData` |
| `Boutquin.OptionPricing.Examples.Shared` | `MarketHelpers` — flat and bootstrapped market construction |
| `Boutquin.Curves.Recipes` | `CurveBuilder`, `StandardCurveRecipes`, `FixtureData` |
| `Boutquin.MarketData.DependencyInjection` | `AddMarketDataKernel` |
| `Boutquin.MarketData.Adapter.NewYorkFed` | SOFR overnight fixings (Example 06) |
| `Boutquin.MarketData.Adapter.UsTreasury` | Treasury par yields (Example 06) |

## Notes

- Examples 01–05 use `FixtureData.CreatePipeline()` — pre-built fixture data with no network calls, deterministic across runs.
- Example 06 fetches live data; it requires network access and degrades gracefully when data is unavailable (prints `SKIPPED` rather than throwing).
- EOD data is published after market close. Example 06 rolls back one business day via `IBusinessCalendar.Advance(today, -1)` to request the last available date.
- L2 disk cache: after the first live fetch, subsequent runs read from the on-disk snapshot (`RetrievalMode.Snapshot`) and skip HTTP calls.
