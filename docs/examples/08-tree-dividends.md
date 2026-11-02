# 08 — Tree Dividends (CRR, With vs. Without Continuous Yield)

## Dividend Modelling in the CRR Tree

The Cox-Ross-Rubinstein tree in this library models dividends as a **continuous yield**
(`q`) via `TreeModelInput.DividendCurve`. This is the standard Merton constant-yield
adjustment: the risk-neutral probability becomes

```
p = (exp((r − q) · dt) − d) / (u − d)
```

where `r` is the risk-free rate and `q` is the continuous dividend yield extracted from
the `DividendCurve` at the expiry date. When `q > 0` the forward is lower
(`F = S · exp((r − q) · T)`), reducing the call price and increasing the put price
relative to the no-dividend case.

**This is not a discrete-dividend model.** Discrete cash dividends on the CRR tree
would require a different construction (jump conditions at ex-dates or the escrowed-spot
adjustment). For discrete-dividend European pricing, see **Example 21**
(`MertonDiscreteDividendPricer`, Chriss §4.9).

**Key types:** `TreeModelInput`, `CoxRossRubinsteinTreeBuilder`, `EuropeanTreePricer`,
`AmericanTreePricer`, `AmericanTreePricer.ComputeExerciseBoundary`, `ExerciseBoundaryResult`

## The Early-Exercise Boundary Shift

An American **call** on a non-dividend-paying stock is never worth exercising early.
The intuition: the call's time value is always positive (holding pays the upside;
delaying costs nothing since the stock pays no dividends), so the holder always prefers
to wait.

When the underlying pays a **continuous dividend yield** (`q > 0`), this breaks down.
The dividend yield lowers the forward: the stock drifts at `r − q` in the risk-neutral
measure instead of `r`. On deep in-the-money nodes at late time steps, the intrinsic
value can exceed the continuation value because waiting effectively costs the holder
the foregone dividend stream. At some critical spot level (the **early-exercise
boundary**), it becomes optimal to exercise the call immediately and invest the proceeds
at `r` rather than continue to hold an instrument that depreciates at rate `q`.

**Direction of the shift (American call):**

| Dividend yield | Early-exercise boundary |
|---|---|
| `q = 0` | **Empty** — exercise is never optimal; American call = European call |
| `q > 0` | **Non-empty** — deep ITM calls exercise early; boundary activates |

The higher the dividend yield, the earlier and deeper in the money exercise becomes
optimal. `AmericanTreePricer.ComputeExerciseBoundary` extracts this boundary
step-by-step from the backward-induction pass: at each time step where at least one
node's intrinsic value strictly exceeds its continuation value, it records the index of
the lowest spot level where exercise is chosen.

## Using It

```csharp
// Build a market with a 3% continuous dividend yield.
var discountCurve = new FlatDiscountCurve(new CurveName("USD-OIS"), valuationDate, Currency.USD, 0.05);
var dividendCurve = new FlatDiscountCurve(new CurveName("DIV"), valuationDate, Currency.USD, 0.03);
// Spot is a Money value in the market currency.
var market = new OptionMarketData(valuationDate, Currency.USD.Amount(100m), 0.20d, discountCurve, dividendCurve);

// Or use the MarketHelpers shorthand:
var market = MarketHelpers.CreateFlatMarket(valuationDate, 100m, sigma: 0.20, riskFreeRate: 0.05, dividendYield: 0.03);

// Build the CRR tree (DividendCurve is consumed by TreeModelInput.DividendYield()).
var input  = TreeModelInput.FromMarketData(market, strike: 100m, expiry, steps: 200);
var tree   = new CoxRossRubinsteinTreeBuilder().Build(input);

// Price European option.
PriceResult euPrice = new EuropeanTreePricer().Price(europeanCall, market, tree);

// Price American option and extract early-exercise boundary.
var amPricer = new AmericanTreePricer();
PriceResult amPrice = amPricer.Price(americanCall, market, tree);
ExerciseBoundaryResult boundary = amPricer.ComputeExerciseBoundary(americanCall, market, tree);

// boundary.IsSuccess == true  when q > 0 and the call is sufficiently deep ITM.
// boundary.Boundaries is a list of (Step, LowestExerciseIndex) pairs.
```

`MarketHelpers.CreateFlatMarket` always supplies a `FlatDiscountCurve` for the dividend
yield, which `TreeModelInput.FromMarketData` picks up as the `DividendCurve`. Passing
`dividendYield: 0.0` is equivalent to no dividend curve.

## Worked Example (Example 23)

`Example 23` prices a six-month ATM European call and a deep-ITM American call on a
200-step CRR tree, comparing `q = 0%` vs. `q = 3%`, then demonstrates the
early-exercise-boundary shift with a 12-month deep-ITM American call at `q = 0%` vs.
`q = 8%`:

```text
Example 23 | Tree dividends (CRR, with vs. without continuous yield, Chriss §6.10)
             Note: the CRR tree models dividends as a continuous yield (q), not discrete cash payments.
Example 23 | European call (q = 0%):    price = 6.8928
Example 23 | European call (q = 3%):    price = 6.0312
Example 23 | European impact of q=3%:   -0.8615  (negative: dividend lowers the forward)
Example 23 | American call K=85 (q = 0%):  price = 17.6588
Example 23 | American call K=85 (q = 3%):  price = 16.2998
Example 23 | Early-exercise boundary (q = 0%): steps active = 0  (expected: 0 — calls never early-exercised without dividends)
Example 23 | Early-exercise boundary (q = 8%): steps active = 200  (expected: > 0 — dividend yield activates early exercise)
Example 23 | Boundary shift: q=0% → empty, q=8% → non-empty (early exercise activated)
```

Run it with:

```bash
dotnet run --project src/Boutquin.OptionPricing.Examples --configuration Release -- --example 23
```

## Accuracy Contract

The boundary-shift assertion is pinned by three tests in `TreeDividendExampleTests`
(`dotnet test --filter FullyQualifiedName~TreeDividendExample`):

1. **`TreeDividendExample_EuropeanCall_PriceWithDividendLowerThanWithout`** — asserts
   that the European call price falls when `q` increases from 0% to 3%, confirming
   the tree incorporates the dividend yield in the CRR risk-neutral probability.

2. **`TreeDividendExample_AmericanCall_PriceWithDividendLowerThanWithout`** — same
   price direction for the American call (the early-exercise premium partially offsets
   the forward reduction, but the call price still falls).

3. **`TreeDividendExample_AmericanCall_BoundaryActivatesWhenDividendAdded`** — the
   core boundary-shift test: asserts that `ComputeExerciseBoundary` returns an
   **empty** boundary at `q = 0%` and a **non-empty** boundary at `q = 8%`, for a
   deep-ITM American call (strike 70, spot 100, 12-month expiry).

All three tests exercise the live production path (`CoxRossRubinsteinTreeBuilder`,
`EuropeanTreePricer`, `AmericanTreePricer`, `AmericanTreePricer.ComputeExerciseBoundary`)
with no stubs.

## Limitations

- **Continuous yield only.** The CRR tree does not support discrete cash dividends
  natively. For discrete dividends, use `MertonDiscreteDividendPricer` (European only,
  Example 21) or a tree with jump conditions at ex-dates (not implemented in v1).
- **American put boundary.** For an American put, dividends *reduce* the early-exercise
  incentive (the stock dropping by a dividend is good for puts, so holding is more
  valuable). The dominant driver of put early exercise is the interest earned on the
  intrinsic value; dividends partially counteract this. See `ExerciseBoundaryTests` for
  the American put boundary test.

## See Also

- **Example 21** — Discrete dividends: Merton escrowed-dividend approximation
  (`MertonDiscreteDividendPricer`, Chriss §4.9)
- **Example 22** — Tree Greeks (Δ, Γ, Θ from the valued lattice, Chriss §7.8)
- **`ExerciseBoundaryTests`** — existing tests for `ComputeExerciseBoundary` (ITM put
  and no-dividend call baselines)

## Reference

N. Chriss, *Black-Scholes and Beyond: Option Pricing Models*, McGraw-Hill, 1997,
§6.10 (dividends in binomial trees) and §6.4 (early exercise of American calls with
dividends).
