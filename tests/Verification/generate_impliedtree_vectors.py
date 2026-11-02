"""
Reference vectors for the ImpliedTrees module.

Both ImpliedBinomialTreeCalibrator (Rubinstein) and ImpliedVolatilityTreeCalibrator
(Derman-Kani) build a genuine smile-consistent implied binomial tree by Derman-Kani
forward induction. A bit-for-bit cross-language replica of that node-by-node solve is
brittle, so this reference instead pins the *correctness properties* a valid implied
tree must satisfy, computed here analytically in Python:

  * the terminal Arrow-Debreu prices sum to the discount factor  e^{-rT}
  * the terminal forward  Σ λ_j S_j  equals  S0·e^{-qT}  (martingale condition)
  * the tree reprices near-the-money European calls (Black-Scholes, the flat-surface
    input) to finite-tree accuracy

The C# verifier builds the tree from the same scenario inputs and asserts these
properties against the Python-computed targets — a true cross-language check of the
construction without re-implementing it in Python.
"""

from __future__ import annotations

import math

from conftest import save_vector


VALUATION_DATE = "2026-01-02"


def _norm_cdf(x: float) -> float:
    return 0.5 * (1.0 + math.erf(x / math.sqrt(2.0)))


def _bs_call(spot: float, strike: float, sigma: float, r: float, q: float, t: float) -> float:
    if t <= 0.0:
        return max(spot - strike, 0.0)
    sigma_sqrt_t = sigma * math.sqrt(t)
    d1 = (math.log(spot / strike) + ((r - q + (0.5 * sigma * sigma)) * t)) / sigma_sqrt_t
    d2 = d1 - sigma_sqrt_t
    return (spot * math.exp(-q * t) * _norm_cdf(d1)) - (strike * math.exp(-r * t) * _norm_cdf(d2))


def _build_surface_points(maturities: list[float], strikes: list[float], sigma: float) -> list[dict]:
    return [
        {"maturity": t, "strike": k, "vol": sigma}
        for t in maturities
        for k in strikes
    ]


def _scenarios() -> list[dict]:
    out: list[dict] = []
    spot = 100.0
    strikes = [80.0, 90.0, 100.0, 110.0, 120.0]
    near_money = [90.0, 95.0, 100.0, 105.0, 110.0]

    for days in (182, 365):
        t = days / 365.0
        for sigma0 in (0.15, 0.25):
            for r, q in ((0.04, 0.00), (0.05, 0.02)):
                for steps in (10, 20):
                    surface_points = _build_surface_points([t], strikes, sigma0)
                    out.append({
                        "valuation_date": VALUATION_DATE,
                        "days_to_expiry": days,
                        "T": t,
                        "spot": spot,
                        "sigma0": sigma0,
                        "r": r,
                        "q": q,
                        "steps": steps,
                        "surface_points": surface_points,
                        "expected": {
                            "discount_factor": math.exp(-r * t),
                            "forward": spot * math.exp(-q * t),
                            "near_money_calls": [
                                {"strike": k, "call": _bs_call(spot, k, sigma0, r, q, t)}
                                for k in near_money
                            ],
                        },
                    })
    return out


def generate() -> None:
    save_vector(
        "implied_tree",
        {
            "meta": {
                "reference": "Analytic correctness properties (AD-sum, forward, BS near-money calls) for the Derman-Kani implied tree",
                "precision_tier": "PRECISION_STATISTICAL",
                "note": "Property-based cross-language check; the C# verifier builds the tree and matches these analytic targets",
            },
            "scenarios": _scenarios(),
        },
    )


if __name__ == "__main__":
    generate()
    print("Wrote implied_tree.json")
