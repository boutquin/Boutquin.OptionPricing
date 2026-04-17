# 03 — Implied Volatility

## IV Inversion

Given an observed market price, the `ImpliedVolatilitySolver` recovers the volatility
that makes the Black-Scholes model reproduce that price. It uses bisection search
over the interval [1e-6, 5.0].

**Key types:** `ImpliedVolatilitySolver`, `ImpliedVolatilityRequest`, `ImpliedVolatilityResult`, `NumericalTolerance`

```csharp
var request = new ImpliedVolatilityRequest(option, market, observedPrice,
    initialGuess: 0.15, NumericalTolerance.Default);
var result = new ImpliedVolatilitySolver().Solve(request);
// result.ImpliedVolatility contains the recovered vol
```

## Smile Construction

Market-observed implied volatilities typically vary by strike, forming a "smile" or
"skew." The `SmileSliceBuilder` assembles a set of (strike, IV) quotes at a fixed
maturity into a `SmileSlice`. Multiple slices at different maturities form a
`VolatilitySurface`.

```csharp
var points = new[] {
    new SurfacePoint(0.5, 80m, 0.28), new SurfacePoint(0.5, 100m, 0.20),
    new SurfacePoint(0.5, 120m, 0.26)
};
var surface = new VolatilitySurfaceBuilder().Build(points);
double vol = surface.GetVol(strike: 95m, maturityInYears: 0.5);
```

## Arbitrage Validation

The `QuoteArbitrageValidator` checks smile slices for:

- **Positivity** — all implied vols must be positive
- **Butterfly spread** — no convexity violations (mid-strike vol cannot exceed
  linear interpolation of neighbors)
- **Calendar spread** — total variance must be non-decreasing in maturity

```csharp
var result = new QuoteArbitrageValidator().Validate(surface);
// result.IsValid, result.Findings
```

## See Also

- **Example 05** — Implied volatility recovery
- **Example 07** — Smile slice construction
