# 07 — Tree Greeks (Lattice Finite Differences)

## Hedge Parameters Without Bump-and-Reprice

The naive way to compute an option's Greeks from a tree is **bump-and-reprice**: perturb
the spot by a small ε, rebuild and re-value the tree, and divide the price change by ε.
That costs one extra full tree valuation per Greek and is sensitive to the choice of ε.

Chriss (*Black-Scholes and Beyond*, §7.8, Display 7.2.1) shows that a recombining binomial
tree already contains the nodes needed to read Δ, Γ, and Θ directly off the **valued
lattice** — no second valuation, no ε. The first two time layers straddle the root in spot,
so the hedge parameters are divided differences of the option value across nodes that the
backward induction has already computed.

**Key types:** `TreeGreeksCalculator`, `LatticeValuationResult`, `EuropeanTreePricer.PriceWithLattice`, `AmericanTreePricer.PriceWithLattice`, `GreekSet`

## The Finite-Difference Scheme

Indices are `(step, up-count)` on the recombining CRR lattice. On this library's top-down
lattice orientation, state index 0 is the highest spot (most up moves).

```
Δ ≈ ( V(1,1) − V(1,0) ) / ( S(1,1) − S(1,0) )                        first-step slope

Γ ≈ ( (V(2,2)−V(2,1))/(S(2,2)−S(2,1))
      − (V(2,1)−V(2,0))/(S(2,1)−S(2,0)) ) / ( (S(2,2)−S(2,0)) / 2 )  curvature at step 2

Θ ≈ ( V(2,1) − V(0,0) ) / ( 2·dt )                                   centre node, two steps forward
```

The centre node `(2,1)` shares the **root's spot level** on a recombining tree (an up
followed by a down returns to the starting spot), which is exactly what makes the two-step
centred difference a clean forward difference in *calendar time* for Θ.

The raw lattice Θ above is an **annualized** derivative. The calculator divides it by 365 so
the reported `GreekSet.Theta` is **per calendar day** — matching the convention of
`BlackScholesGreeks.Theta`, so the two are directly comparable.

## Using It

Value the tree with the lattice-returning overload, then hand the result to the calculator:

```csharp
// option.Strike is a Money; the tree builder takes the decimal amount.
var input = TreeModelInput.FromMarketData(market, option.Strike.Amount, expiry, steps: 400);
var tree  = new CoxRossRubinsteinTreeBuilder().Build(input);

// PriceWithLattice retains every layer of option values (Price returns only the root).
var valued = new EuropeanTreePricer().PriceWithLattice(option, market, tree);

GreekSet greeks = new TreeGreeksCalculator().Compute(valued);
// greeks.Delta, greeks.Gamma, greeks.Theta are populated; Vega and Rho are null.
```

`AmericanTreePricer.PriceWithLattice` produces the same `LatticeValuationResult` shape for
American options (each node holding `max(continuation, exercise)`), so the identical
calculator reads American Greeks off the early-exercise-adjusted lattice.

## Accuracy Contract

For a **European** option the lattice Δ, Γ, and Θ converge to the closed-form Black-Scholes
Greeks as the step count grows. The guarantee is pinned by a convergence test
(`EuropeanCall_DeltaGammaTheta_ConvergeToAnalyticalWithinOnePercent` and its put sibling),
which asserts all three within **1%** of the analytical values at ≥ 200 steps.

At small step counts the divided differences stay **finite** (no division by zero, no `NaN`):
adjacent CRR spot levels are always distinct for `u > d`, so every denominator is non-zero.
`MinimalStepCounts_ProduceFiniteGreeks` checks this down to a 2-step tree (the smallest
lattice the Display 7.2.1 differences can read).

For an **American put**, `AmericanPut_DeltaInNegativeUnitInterval_GammaPositive` asserts the
sign/range invariants: Δ ∈ [−1, 0] (value falls as spot rises, bounded below by −1) and
Γ > 0 (convexity).

## Worked Example (Example 22)

`Example 22` builds a 400-step CRR tree for a six-month at-the-money option (spot 100, strike
100, σ = 20%, r = 5%, no dividend), reads the European-call Greeks off the lattice next to the
analytical Black-Scholes Greeks, and reads the American-put Greeks off its early-exercise
lattice:

```text
Example 22 | European call lattice price:  6.8963
Example 22 | European Delta (tree / BS):   0.597806 / 0.597866
Example 22 | European Gamma (tree / BS):   0.027374 / 0.027319
Example 22 | European Theta (tree / BS):   -0.022243 / -0.022214  (per calendar day)
Example 22 | American put lattice price:   4.6593
Example 22 | American put Delta:           -0.432356  (in [-1, 0])
Example 22 | American put Gamma:           0.030865  (> 0)
Example 22 | Vega / Rho:                   null (require repricing — out of lattice scope)
```

Run it with:

```bash
dotnet run --project src/Boutquin.OptionPricing.Examples --configuration Release -- --example 22
```

The tree Greeks agree with Black-Scholes to roughly four significant figures at 400 steps,
without ever re-pricing the tree.

## Limitations

- **Δ, Γ, Θ only.** Vega and Rho are **not** lattice-extractable from a single valued tree —
  they are derivatives with respect to volatility and the rate, which change the tree
  *geometry* itself and therefore require re-pricing with a perturbed tree. They are returned
  as `null` in the `GreekSet`. (`GreekSet` carries no Lambda member, so no Lambda is reported.)
- **At least two steps.** The Γ and Θ differences need the step-2 layer; `Compute` throws
  `ArgumentException` on a tree with fewer than two steps (pinned by
  `Compute_TreeWithFewerThanTwoSteps_Throws`).
- **Convergence is asymptotic.** The 1% accuracy contract holds at ≥ 200 steps; at very low
  step counts the Greeks are finite but coarse.

## See Also

- **Example 02** — Analytical term-structure Black-Scholes Greeks (the closed-form reference)
- **Example 23** — CRR trees with vs. without discrete dividends (American early-exercise shift)

## Reference

N. Chriss, *Black-Scholes and Beyond: Option Pricing Models*, McGraw-Hill, 1997, §7.8,
Display 7.2.1 (extracting Δ, Γ, Θ from the binomial lattice).
