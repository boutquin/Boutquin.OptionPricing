"""
Reference vectors for the Smile / VolatilitySurface machinery.

Scope — we verify the pieces whose contract is independent of the specific
interpolator chosen by the C# side:

  * VolatilitySurfaceBuilder.Build  — structural: slices are maturity-ordered,
    strikes within each slice are ascending.
  * VolatilitySurface.GetVol grid reads — at a node coordinate the returned
    vol must equal the stored ImpliedVolatility exactly.
  * Two-slice / two-strike linear reduction — when a slice has exactly two
    strikes or the surface has exactly two maturities, the C# monotone-cubic
    collapses to linear and the C# cubic spline short-circuits to
    TwoPointLinearInterpolator. Python computes the same linear values.
  * QuoteArbitrageValidator — arbitrage-free grids flagged IsValid=True,
    butterfly / calendar-spread violators return IsValid=False with at
    least one finding.

Full bivariate interpolation of the C# monotone-cubic Hermite (arXiv
2402.01324) + modified-not-a-knot cubic spline (arXiv 2507.05083) against a
scipy reference is out of scope — those specific numerical schemes are
Boutquin.Numerics-proprietary. Grid-point + two-node-linear verification
pins down every path that doesn't depend on the specific interpolator.
"""

from __future__ import annotations

import numpy as np

from conftest import save_vector


def _structural_vectors() -> list[dict]:
    # Input: shuffled points. Expected: maturity-ascending slices, strike-ascending
    # within each slice. Mirrors VolatilitySurfaceBuilder.Build.
    out: list[dict] = []
    points_a = [
        {"maturity": 0.50, "strike": 110.0, "vol": 0.22},
        {"maturity": 0.25, "strike": 100.0, "vol": 0.20},
        {"maturity": 0.50, "strike": 90.0, "vol": 0.28},
        {"maturity": 0.25, "strike": 90.0, "vol": 0.25},
        {"maturity": 0.50, "strike": 100.0, "vol": 0.23},
        {"maturity": 0.25, "strike": 110.0, "vol": 0.18},
    ]
    expected_a = [
        {"maturity": 0.25, "strikes": [90.0, 100.0, 110.0], "vols": [0.25, 0.20, 0.18]},
        {"maturity": 0.50, "strikes": [90.0, 100.0, 110.0], "vols": [0.28, 0.23, 0.22]},
    ]
    out.append({"name": "two_maturity_three_strike", "points": points_a, "expected_slices": expected_a})

    points_b = [
        {"maturity": 1.00, "strike": 100.0, "vol": 0.30},
        {"maturity": 0.10, "strike": 100.0, "vol": 0.18},
        {"maturity": 0.50, "strike": 100.0, "vol": 0.24},
    ]
    expected_b = [
        {"maturity": 0.10, "strikes": [100.0], "vols": [0.18]},
        {"maturity": 0.50, "strikes": [100.0], "vols": [0.24]},
        {"maturity": 1.00, "strikes": [100.0], "vols": [0.30]},
    ]
    out.append({"name": "three_maturity_single_strike", "points": points_b, "expected_slices": expected_b})
    return out


def _grid_read_vectors() -> list[dict]:
    # Build a small surface and query at grid coordinates — must return the
    # stored vol regardless of which interpolator the C# side picks.
    grid = [
        {"maturity": 0.25, "strike": 90.0, "vol": 0.25},
        {"maturity": 0.25, "strike": 100.0, "vol": 0.20},
        {"maturity": 0.25, "strike": 110.0, "vol": 0.18},
        {"maturity": 0.50, "strike": 90.0, "vol": 0.28},
        {"maturity": 0.50, "strike": 100.0, "vol": 0.23},
        {"maturity": 0.50, "strike": 110.0, "vol": 0.22},
        {"maturity": 1.00, "strike": 90.0, "vol": 0.30},
        {"maturity": 1.00, "strike": 100.0, "vol": 0.25},
        {"maturity": 1.00, "strike": 110.0, "vol": 0.24},
    ]
    return [{"grid": grid, "queries": [{"strike": p["strike"], "maturity": p["maturity"], "expected_vol": p["vol"]} for p in grid]}]


def _two_node_linear_vectors() -> list[dict]:
    # C# docstring: "With exactly two strikes, the monotone-cubic Hermite
    # reduces algebraically to linear interpolation, so two-point slices
    # behave identically to the prior linear implementation."
    #
    # Similarly two-maturity surfaces short-circuit to TwoPointLinearInterpolator.
    # These cases are bit-compatible with a plain numpy linear blend.
    out: list[dict] = []

    # Two-strike slice (single maturity → strike axis only)
    two_strike = {
        "grid": [
            {"maturity": 0.50, "strike": 90.0, "vol": 0.25},
            {"maturity": 0.50, "strike": 110.0, "vol": 0.15},
        ],
        "queries": [],
    }
    for k in (95.0, 100.0, 105.0):
        weight = (k - 90.0) / (110.0 - 90.0)
        expected = 0.25 + weight * (0.15 - 0.25)
        two_strike["queries"].append({"strike": k, "maturity": 0.50, "expected_vol": expected})
    out.append({"name": "two_strike_linear", **two_strike})

    # Two-maturity surface (two slices, each with a single strike)
    two_maturity = {
        "grid": [
            {"maturity": 0.25, "strike": 100.0, "vol": 0.18},
            {"maturity": 1.00, "strike": 100.0, "vol": 0.30},
        ],
        "queries": [],
    }
    for t in (0.50, 0.75):
        weight = (t - 0.25) / (1.00 - 0.25)
        expected = 0.18 + weight * (0.30 - 0.18)
        two_maturity["queries"].append({"strike": 100.0, "maturity": t, "expected_vol": expected})
    out.append({"name": "two_maturity_linear", **two_maturity})

    return out


def _arbitrage_vectors() -> list[dict]:
    # Valid: positive vols, convex smile (butterfly OK), total variance
    # non-decreasing in maturity.
    valid_grid = [
        {"maturity": 0.25, "strike": 90.0, "vol": 0.25},
        {"maturity": 0.25, "strike": 100.0, "vol": 0.20},
        {"maturity": 0.25, "strike": 110.0, "vol": 0.22},
        {"maturity": 0.50, "strike": 90.0, "vol": 0.27},
        {"maturity": 0.50, "strike": 100.0, "vol": 0.22},
        {"maturity": 0.50, "strike": 110.0, "vol": 0.24},
    ]
    # Butterfly arbitrage: middle vol > linear blend of neighbors (concave dip
    # that would give negative butterfly call spread).
    butterfly_arb = [
        {"maturity": 0.25, "strike": 90.0, "vol": 0.10},
        {"maturity": 0.25, "strike": 100.0, "vol": 0.50},  # spike above linear blend
        {"maturity": 0.25, "strike": 110.0, "vol": 0.10},
    ]
    # Calendar arbitrage: total variance decreases from T=0.25 to T=0.50.
    # σ²₁·T₁ = 0.30² · 0.25 = 0.0225  vs  σ²₂·T₂ = 0.15² · 0.50 = 0.01125 → violation.
    calendar_arb = [
        {"maturity": 0.25, "strike": 100.0, "vol": 0.30},
        {"maturity": 0.50, "strike": 100.0, "vol": 0.15},
    ]
    # Non-positive vol
    non_positive = [
        {"maturity": 0.25, "strike": 90.0, "vol": 0.20},
        {"maturity": 0.25, "strike": 100.0, "vol": 0.0},   # non-positive
        {"maturity": 0.25, "strike": 110.0, "vol": 0.22},
    ]
    return [
        {"name": "valid_surface", "grid": valid_grid, "expected_valid": True, "min_findings": 0},
        {"name": "butterfly_arbitrage", "grid": butterfly_arb, "expected_valid": False, "min_findings": 1},
        {"name": "calendar_arbitrage", "grid": calendar_arb, "expected_valid": False, "min_findings": 1},
        {"name": "non_positive_vol", "grid": non_positive, "expected_valid": False, "min_findings": 1},
    ]


def generate() -> None:
    save_vector(
        "smile",
        {
            "meta": {
                "reference": "structural + grid-point + two-node-linear + arbitrage expectations",
                "precision_tier": "PRECISION_NUMERIC (grid reads exact, two-node linear deterministic)",
                "interpolator_scope": "C# uses MonotoneCubicInterpolator + adaptive CubicSplineInterpolator; verifier exercises only paths independent of those specific schemes (grid reads and two-node reductions)",
            },
            "structural": _structural_vectors(),
            "grid_reads": _grid_read_vectors(),
            "two_node_linear": _two_node_linear_vectors(),
            "arbitrage": _arbitrage_vectors(),
        },
    )


if __name__ == "__main__":
    generate()
    print("Wrote smile.json")
