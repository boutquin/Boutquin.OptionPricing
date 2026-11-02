# 01 — Vanilla Pricing

## Black-Scholes Pricing

The `BlackScholesPricer` computes European option prices under constant volatility,
risk-free rate, and dividend yield assumptions. It takes an `EuropeanOption` contract
and an `OptionMarketData` snapshot, returning a `PriceResult` with the model price.

**Key types:** `BlackScholesPricer`, `BlackScholesGreeks`, `EuropeanOption`, `OptionMarketData`, `PriceResult`

### Pricing a European Call

```csharp
// The contract carries its currency; the strike and spot are Money values in that currency.
var option = new EuropeanOption(CallPut.Call, strike: 100m, Currency.USD, expiryDate);
var market = new OptionMarketData(valuationDate, Currency.USD.Amount(100m), flatVolatility: 0.20,
    discountCurve, dividendCurve);
var result = new BlackScholesPricer().Price(option, market);
// result.Price is a Money premium in the contract currency (result.Price.Amount is the value).
```

The contract currency and the market spot currency must match: pricing a USD contract
against a EUR market throws `CurrencyMismatchException` rather than silently producing a
meaningless cross-currency number.

## Greeks

`BlackScholesGreeks` computes first- and second-order sensitivities (Delta, Gamma, Vega,
Theta, Rho) from the same inputs. These measure how the option price changes with respect
to spot, volatility, time, and rates.

```csharp
var greeks = new BlackScholesGreeks().CalculateGreeks(option, market);
// greeks.Delta, greeks.Gamma, greeks.Vega
```

## Put-Call Parity

`PutCallParity` verifies the fundamental no-arbitrage relationship between call and put
prices on the same underlying with matching strike and expiry:

C − P = S × e^(−qT) − K × e^(−rT)

```csharp
decimal impliedCall = PutCallParity.ImpliedCallPrice(putPrice, spot, strike, market, expiryDate);
```

## See Also

- **Example 01** — Black-Scholes vanilla pricing
- **Example 02** — Greeks and parity
