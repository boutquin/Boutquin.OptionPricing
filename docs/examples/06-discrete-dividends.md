# 06 — Discrete Dividends (Merton Escrowed-Dividend Model)

## Known Cash Dividends

Black-Scholes assumes a *continuous* dividend yield: a smooth stream of payments
proportional to the spot. Real equities pay **discrete cash dividends** — a fixed dollar
amount per share on a handful of ex-dividend dates. On each ex-date the stock price drops
by roughly the dividend amount, which a continuous-yield model cannot capture exactly.

Merton's **escrowed-dividend approximation** (Chriss, *Black-Scholes and Beyond*, §4.9)
adapts the closed-form Black-Scholes formula to known discrete dividends without leaving the
analytical world.

**Key types:** `MertonDiscreteDividendPricer`, `DividendSchedule`, `DividendPayment`

## The Escrowed-Spot Model

The idea: split the stock price into a **riskless escrowed component** (the present value
of the dividends still to be paid before expiry) and a **stochastic component** that follows
the usual lognormal process. The option is written only on the stochastic part.

```
PV(divs) = Σ_i  d_i · DF(t_i)        present value of dividends with ex-dates t_i < T
S*       = S₀ − PV(divs)             escrowed (stochastic) spot
σ*       = (S₀ / S*) · σ             volatility uplift
```

where `d_i` is the cash dividend on ex-date `t_i`, `DF(t_i)` is the risk-free discount factor
from the market's discount curve, `S₀` is the current spot, and `σ` is the flat volatility.

The European option is then priced by the ordinary `BlackScholesPricer` on the adjusted
spot `S*` and uplifted volatility `σ*`:

```csharp
var schedule = new DividendSchedule(new[]
{
    new DividendPayment(valuationDate.AddDays(90), 0.80m),
    new DividendPayment(valuationDate.AddDays(270), 0.80m),
});

// result.Price is a Money premium in the contract currency (result.Price.Amount for the value).
// When the present value of dividends meets or exceeds the spot, the result is an
// unsupported-scenario PriceResult whose zero price is denominated in the contract currency.
var result = new MertonDiscreteDividendPricer(schedule).Price(option, market);
```

## Why the Volatility Is Uplifted

The volatility uplift `σ* = (S₀ / S*) · σ` is the subtle part. Removing the escrowed
dividends shrinks the stochastic base from `S₀` to `S*`. If we kept the same *return*
volatility `σ`, the smaller base would carry a smaller *dollar* dispersion at expiry — the
option would be under-priced. Scaling `σ` up by `S₀ / S*` restores the original dollar
variance of the terminal price.

A direct consequence: **`σ* > σ` strictly whenever `PV(divs) > 0`.** The library asserts
this in `PositivePvDividends_UpliftsVolatilityStrictly`.

## Diagnostics

Every result carries the escrowed intermediates for audit, alongside
`ModelName = "Merton-DiscreteDividend"`:

| Diagnostic key | Meaning |
|----------------|---------|
| `pvDividends` | Present value of the captured dividends |
| `escrowedSpot` | `S* = S₀ − PV(divs)` |
| `upliftedVolatility` | `σ* = (S₀ / S*) · σ` |
| `originalSpot` / `originalVolatility` | The un-adjusted inputs |
| `dividendCount` | Number of dividends with ex-date before expiry |

## Worked Example (Example 21)

`Example 21` prices a one-year at-the-money call (spot 100, strike 100, σ = 20%, r = 5%) with
two $0.80 cash dividends (ex-dates at 90 and 270 days), and compares it to the no-dividend
Black-Scholes price:

```text
Example 21 | No-dividend BS call price:   10.4506
Example 21 | Merton escrowed call price:  9.5997
Example 21 | PV(dividends):               1.561149
Example 21 | Escrowed spot S*:            98.438851
Example 21 | Uplifted volatility sigma*:  0.203172
Example 21 | Dividend impact:             -0.8509
```

Run it with:

```bash
dotnet run --project src/Boutquin.OptionPricing.Examples --configuration Release -- --example 21
```

The dividends lower the call by about $0.85 — the holder of a call does **not** receive the
dividends, so the lower effective forward reduces the call's value, exactly as the
continuous cost-of-carry effect (Example 18) shows for a dividend *yield*.

## Edge Cases And Limitations

- **Empty schedule** — or a schedule whose ex-dates all fall on or after expiry — yields
  `PV(divs) = 0`, so `S* = S₀` and `σ* = σ`, and the price is **identical** to
  `BlackScholesPricer` (pinned to 1e-12 by
  `EmptySchedule_EqualsBlackScholesPricer_ToOneInTwelve`). Dividends paid after the option
  expires are not captured by the option holder.
- **Dividends exceeding spot** — when `PV(divs) ≥ S₀` the escrowed spot would be
  non-positive and the model is undefined. The pricer returns an
  `UnsupportedScenario` result for "Merton-DiscreteDividend" rather than throwing
  (`DividendsExceedingSpot_ReturnsUnsupportedScenario`).
- **Approximation, not exact** — the escrowed model is an analytical *approximation*; the
  true discrete-dividend dynamics are not lognormal. It is most accurate for small dividends
  well inside the option's life. **American** discrete-dividend options are priced exactly by
  the binomial tree pricers (which model the spot drop on each ex-date directly), not by this
  closed form — see Example 23.

## See Also

- **Example 18** — Continuous dividend yield impact on Black-Scholes (the cost-of-carry view)
- **Example 23** — CRR trees with vs. without discrete dividends (American early-exercise shift)

## Reference

N. Chriss, *Black-Scholes and Beyond: Option Pricing Models*, McGraw-Hill, 1997, §4.9
(the escrowed-dividend / Merton adjustment).
