# 04 — Implied Trees

## Motivation

Flat-vol CRR trees assume constant volatility across all strikes. In reality, the
volatility smile means options at different strikes trade at different implied vols.
Implied trees calibrate their transition probabilities or node spacing to reproduce
the observed smile, yielding smile-consistent local volatilities.

## Derman-Kani Calibration

The `ImpliedVolatilityTreeCalibrator` implements the Derman-Kani algorithm: it starts
with a CRR lattice backbone, then uses forward induction of Arrow-Debreu state prices
to extract local volatilities consistent with the market smile.

**Key types:** `ImpliedVolatilityTreeCalibrator`, `ImpliedVolatilityTreeCalibrationInput`, `ImpliedTreeResult`

```csharp
var input = new ImpliedVolatilityTreeCalibrationInput(surface, steps: 30, market);
var result = new ImpliedVolatilityTreeCalibrator().Calibrate(input);
// result.Tree, result.LocalVolatilities, result.ArrowDebreuPrices
```

## Rubinstein Calibration

The `ImpliedBinomialTreeCalibrator` implements Rubinstein's approach: it also starts
from a CRR lattice but uses a different forward induction scheme to compute implied
transition probabilities. Both methods produce an `ImpliedTreeResult` with the
calibrated tree, local vols, and Arrow-Debreu prices.

```csharp
var rubResult = new ImpliedBinomialTreeCalibrator().Calibrate(input);
```

## Comparing Methods

At-the-money, both methods produce similar local volatilities. The differences appear
in the wings (deep in/out of the money) where the smile shape has the most impact on
local vol extraction.

## See Also

- **Example 08** — Implied tree comparison (DK vs Rubinstein)
