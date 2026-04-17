# Contributing to Boutquin.OptionPricing

Thank you for considering contributing to Boutquin.OptionPricing! Whether it's reporting a bug, proposing a feature, or submitting a pull request, your input is welcome.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [How to Contribute](#how-to-contribute)
  - [Reporting Bugs](#reporting-bugs)
  - [Suggesting Enhancements](#suggesting-enhancements)
  - [Contributing Code](#contributing-code)
- [Style Guides](#style-guides)
  - [Git Commit Messages](#git-commit-messages)
  - [C# Style Guide](#c-style-guide)
  - [Documentation Style Guide](#documentation-style-guide)
  - [Pricing Correctness](#pricing-correctness)
- [Pull Request Process](#pull-request-process)
- [License](#license)
- [Community](#community)

## Code of Conduct

This project adheres to the Contributor Covenant [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Report unacceptable behavior through [GitHub Issues](https://github.com/boutquin/Boutquin.OptionPricing/issues).

## How to Contribute

### Reporting Bugs

Open an issue on the [Issues](https://github.com/boutquin/Boutquin.OptionPricing/issues) page with:

- A clear and descriptive title.
- Steps to reproduce the issue (ideally a minimal failing code snippet).
- Expected and actual output, including numerical values and any relevant model parameters (spot, strike, expiry, volatility, rate).
- Reference values (from a textbook derivation, Python/numpy/scipy, or a known-good implementation) when asserting pricing correctness.
- Environment: OS, .NET runtime version, package version.

### Suggesting Enhancements

Open an issue describing:

- The pricing model, calibration algorithm, or Greeks calculator you would like to see added.
- A primary reference (paper, textbook chapter, arXiv number) with the algorithm's published form.
- The intended consumer and exercise style(s) supported (European, American, barrier, etc.).
- Any trade-offs: convergence properties, memory profile, model assumptions, limitations.

### Contributing Code

1. **Fork the repository** and clone your fork locally.
   ```bash
   git clone https://github.com/your-username/Boutquin.OptionPricing.git
   cd Boutquin.OptionPricing
   ```

2. **Create a feature branch**:
   ```bash
   git checkout -b feature-or-bugfix-name
   ```

3. **Implement the change** following the style guides below.

4. **Add tests** covering the new behavior. For option-pricing code, prefer:
   - xUnit + FluentAssertions unit tests under `tests/Boutquin.OptionPricing.<Package>.Tests/` with explicit tolerances and named model parameters.
   - Cross-language verification tests under `tests/Boutquin.OptionPricing.Tests.Verification/` when a Python reference (QuantLib / scipy / numpy) can establish ground truth.
   - Property-based tests under `tests/Boutquin.OptionPricing.PropertyTests/` for invariants that hold across input ranges (put-call parity, monotonicity, convergence).
   - Integration tests under `tests/Boutquin.OptionPricing.IntegrationTests/` when the change affects the end-to-end path from market data through curve construction to pricing.
   - Architecture assertions under `tests/Boutquin.OptionPricing.ArchitectureTests/` when the change introduces new assemblies, new external dependencies, or new cross-tier references.

5. **Record the public API surface** in `src/<Package>/PublicAPI.Unshipped.txt`. The `PublicAPI` analyzer enforces this at build time.

6. **Update `CHANGELOG.md`** under the `[Unreleased]` section.

7. **Run the full gate** before opening a PR:
   ```bash
   dotnet build Boutquin.OptionPricing.slnx --configuration Release
   dotnet test Boutquin.OptionPricing.slnx --configuration Release
   dotnet format Boutquin.OptionPricing.slnx --verify-no-changes
   ```

8. **Push and open a pull request**.

## Style Guides

### Git Commit Messages

- Use the present tense ("Add Brent solver" not "Added Brent solver").
- Use the imperative mood ("Move IV solver to Calibration" not "Moves IV solver to Calibration").
- Limit the first line to 72 characters.
- Reference issues and pull requests where applicable.

### C# Style Guide

- Follow the conventions documented in `CLAUDE.md` and `.editorconfig` at the repository root.
- Public types are `sealed` unless they are interfaces or records (enforced by architecture tests).
- OptionPricing must not reference `Boutquin.Trading`. The allowed chain is `Boutquin.Numerics` → `Boutquin.Curves` → `Boutquin.OptionPricing`. Do not add a reference to any domain package outside this chain.
- Litmus test for any new type: "Does this encode an option pricing model, a volatility calibration algorithm, or a Greeks calculation?" If it belongs in the curve or numerics layer, it does not belong here.

### Documentation Style Guide

All public API additions must satisfy the in-code documentation bar:

- `<summary>` on every public type, constructor, method, property, and enum member.
- `<param>`, `<returns>`, and `<remarks>` per the required-elements checklist.
- No banned boilerplate phrases ("Provides the ... functionality", "Executes the ... operation", "Gets or sets the ... for this instance", "Input value for ...", "/// Executes ...", "Operation result.", "/// Gets the ...").
- Pricing and calibration references: name the textbook chapter, paper, author, year, and (where applicable) arXiv number.
- Elevated standards apply to pricers, Greeks calculators, root solvers, and calibrators — each must name the model, the exercise styles supported, the convergence or accuracy contract, and any degenerate-input fallback behavior.

Validation commands (must return zero matches before opening a PR):

```bash
# Enforce banned-phrase policy.
rg -n "Provides the .* functionality and related domain behavior|Executes the .* operation for this component|The .* input value for the operation|Gets or sets the .* for this instance" src --glob '*.cs'

# Enforce low-signal phrase policy.
rg -n "Input value for <paramref name=|/// Executes |Operation result\." src --glob '*.cs'

# Enforce accessor-verb property doc policy.
rg -n "/// Gets the " src --glob '*.cs'
```

### Pricing Correctness

- State tolerances explicitly in docs and tests. Do not assert equality on floating-point prices or Greeks.
- Document model assumptions (exercise style, dividend treatment, cost-of-carry convention, volatility input) on every pricer and calibrator.
- Put-call parity: every European pricer must have a test asserting parity holds to within tolerance across a range of moneyness levels.
- American vs European: tests must verify that the American put price is always ≥ the European put price at the same inputs.
- Convergence: tree-based pricers must have a convergence test asserting prices approach Black-Scholes as step count increases.
- Implied volatility round-trip: IV solvers must have a test that inverts a model price, re-prices, and recovers the original price to tolerance.
- Calibrated surfaces: smile and implied-tree calibrators must assert that butterfly and calendar arbitrage are absent from valid inputs.

## Pull Request Process

1. **Ensure the full gate passes**: build (warnings-as-errors), all tests (unit, verification, property, integration, architecture), and `dotnet format --verify-no-changes`.
2. **Describe your changes** in the PR body: reference the issue, summarize the model or fix, cite the primary reference, and note any `PublicAPI.Unshipped.txt` entries added.
3. **Review process**: maintainers will review for correctness, style, and architectural fit. You may be asked to tighten tolerances, add reference vectors, or rename symbols.
4. **Merge**: once approved, a maintainer merges the PR. Releases are cut separately via the dual-repo squash workflow on the public repository.

## License

By contributing to Boutquin.OptionPricing, you agree that your contributions are licensed under the Apache 2.0 License.

## Community

Join the [GitHub Discussions](https://github.com/boutquin/Boutquin.OptionPricing/discussions) to ask questions, propose models, and share usage patterns.

---

Thank you for contributing!
