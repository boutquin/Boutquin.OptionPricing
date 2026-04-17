"""Orchestrator: run every verification vector generator in the folder."""

from __future__ import annotations

import generate_blackscholes_vectors
import generate_parity_vectors
import generate_binomial_vectors
import generate_iv_vectors
import generate_smile_vectors
import generate_barrier_vectors
import generate_impliedtree_vectors
import generate_hedging_vectors


GENERATORS = [
    ("Black-Scholes", generate_blackscholes_vectors),
    ("Parity", generate_parity_vectors),
    ("Binomial", generate_binomial_vectors),
    ("Implied Volatility", generate_iv_vectors),
    ("Smile / Surface", generate_smile_vectors),
    ("Barrier", generate_barrier_vectors),
    ("Implied Tree", generate_impliedtree_vectors),
    ("Hedging", generate_hedging_vectors),
]


def main() -> None:
    for name, module in GENERATORS:
        module.generate()
        print(f"  ✓ {name}")
    print("All OptionPricing verification vectors regenerated.")


if __name__ == "__main__":
    main()
