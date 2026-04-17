"""
Shared helpers and precision tiers for Boutquin.OptionPricing cross-language
verification.

These generators produce JSON reference vectors that the C# test project
``Boutquin.OptionPricing.Tests.Verification`` consumes to assert that the
C# implementations match independent Python references (scipy / numpy)
within the tiered tolerances defined below.

Run ``python generate_vectors.py`` (or individual ``generate_*.py`` scripts)
first to populate ``vectors/`` before executing the C# test assembly.
"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np


# ---------------------------------------------------------------------------
# Precision tiers — match the tolerance to the calculation family
# ---------------------------------------------------------------------------
PRECISION_EXACT = 1e-10          # Pure algebra (put-call parity, payoffs).
PRECISION_NUMERIC = 1e-6         # Closed-form with normal CDF, identical-algorithm tree.
PRECISION_STATISTICAL = 1e-4     # Monte Carlo, tree → analytic-limit convergence.


VECTORS_DIR = Path(__file__).parent / "vectors"


def save_vector(name: str, data: dict) -> None:
    """Serialize a vector dict as JSON into ``vectors/<name>.json``."""

    VECTORS_DIR.mkdir(exist_ok=True)
    path = VECTORS_DIR / f"{name}.json"

    def convert(obj):
        if isinstance(obj, np.ndarray):
            return obj.tolist()
        if isinstance(obj, np.floating):
            return float(obj)
        if isinstance(obj, np.integer):
            return int(obj)
        raise TypeError(f"Cannot serialize {type(obj)!r}")

    with open(path, "w") as f:
        json.dump(data, f, indent=2, default=convert)


def load_vector(name: str) -> dict:
    path = VECTORS_DIR / f"{name}.json"
    with open(path) as f:
        return json.load(f)
