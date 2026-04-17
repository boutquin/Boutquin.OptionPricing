# 02 — Tree Convergence

## CRR Binomial Tree

The Cox-Ross-Rubinstein tree discretizes the continuous Black-Scholes process into a
recombining lattice. At each time step, the spot moves up by factor `u = exp(σ√Δt)`
or down by `d = 1/u`, with risk-neutral probability `p = (growth − d) / (u − d)`.

**Key types:** `CoxRossRubinsteinTreeBuilder`, `JarrowRuddTreeBuilder`, `FlexibleBinomialTreeBuilder`, `BinomialTree`, `TreeModelInput`

### Building a Tree

The `TreeModelInput.FromMarketData` factory extracts curves, spot, and volatility from an `OptionMarketData` instance — avoiding manual parameter duplication:

```csharp
var input = TreeModelInput.FromMarketData(market, strike, expiryDate, steps: 200);
var tree = new CoxRossRubinsteinTreeBuilder().Build(input);
```

### Jarrow-Rudd Alternative

The Jarrow-Rudd parameterization sets `p = 0.5` and adjusts the up/down factors to
match drift and volatility. Use `FlexibleBinomialTreeBuilder` to select at runtime:

```csharp
var builder = new FlexibleBinomialTreeBuilder(TreeParameterization.JarrowRudd);
var tree = builder.Build(input);
```

## Convergence to Black-Scholes

As the number of steps increases, tree prices converge to the analytic Black-Scholes
price at rate O(1/N). The `EuropeanTreePricer` performs backward induction on the
tree to compute the discounted expected payoff.

```csharp
var treePrice = new EuropeanTreePricer().Price(option, market, tree);
```

## American Exercise

The `AmericanTreePricer` adds an early exercise check at each node: at every step,
the option value is the maximum of continuation value and immediate exercise value.
American puts on non-dividend-paying stocks always carry an early exercise premium
over their European counterparts.

```csharp
var americanPrice = new AmericanTreePricer().Price(americanOption, market, tree);
```

## See Also

- **Example 03** — Binomial convergence
- **Example 04** — American put early exercise
