# 05 — Barrier Pricing

## Barrier Options

A barrier option activates (knock-in) or deactivates (knock-out) when the underlying
crosses a specified barrier level. Combined with direction (up/down) and type (call/put),
this gives 8 variants: up-in/out call/put × down-in/out call/put.

**Key types:** `BarrierOption`, `BarrierTreePricer`, `SmileAwareBarrierPricer`, `BarrierMonitoringAdjustments`

## Flat-Vol Tree Pricing

The `BarrierTreePricer` performs backward induction on a CRR tree with barrier
monitoring at each node. Knock-out nodes receive zero continuation value. Knock-in
options are derived via barrier parity: V_in = V_vanilla − V_out.

```csharp
// The contract currency follows the strike; the barrier level stays a decimal.
var option = new BarrierOption(CallPut.Call, strike: 100m, Currency.USD, barrierLevel: 80m,
    BarrierDirection.Down, BarrierKnockType.Out, expiryDate);
var result = new BarrierTreePricer().Price(option, market, tree);
// result.Price is a Money premium in the contract currency.
```

## Smile-Aware Pricing

Barrier options are particularly sensitive to the volatility smile because the barrier
level often lies in the wing of the smile where flat-vol assumptions break down. The
`SmileAwareBarrierPricer` uses a calibrated implied tree instead of a flat-vol CRR tree:

```csharp
var calibResult = new ImpliedVolatilityTreeCalibrator().Calibrate(calibInput);
var smilePrice = new SmileAwareBarrierPricer().Price(option, market, calibResult);
```

The difference between flat-vol and smile-aware prices quantifies the "skew impact" —
the pricing error from ignoring the smile.

## Monitoring Adjustments

Real barrier options are monitored at discrete dates (daily closes), not continuously.
The Broadie-Glasserman-Kou correction adjusts the barrier level to account for the
gap between discrete and continuous monitoring:

H_adjusted = H × exp(±β × σ × √(T/m))

where β ≈ 0.5826, σ is volatility, T is time to expiry, and m is the number of
monitoring dates.

```csharp
decimal adjusted = BarrierMonitoringAdjustments.AdjustedBarrierLevel(
    barrierLevel, direction, volatility, timeToExpiry, monitoringDates);
```

## See Also

- **Example 09** — All 8 barrier variants on flat-vol tree
- **Example 10** — Flat-vol vs smile-aware comparison
