"""
Reference vectors for the ImpliedTrees module.

Both ImpliedBinomialTreeCalibrator (Rubinstein) and
ImpliedVolatilityTreeCalibrator (Derman-Kani) in the current C# codebase
build a CRR-structured lattice with:

  * u = exp(σ₀ · √dt),  d = 1/u
  * spot lattice: S₀ · u^(step-j) · d^j
  * probability clamped to [ε, 1-ε] at each node (ε = 1e-6)
  * AD forward induction: AD(step+1, j) = AD(step, j-1)·(1-p)·DF + AD(step, j)·p·DF
  * local vols: σ_local(step, j) = ln(S_up / S_down) / (2√dt) = σ₀ (uniform)

The Python reference replicates these expressions verbatim and asserts the
C# output at PRECISION_NUMERIC. The bespoke Derman-Kani *paper algorithm*
(probability-solving from market call prices) is not what the current C#
calibrator does — the code falls back to CRR probabilities for numerical
stability, so the verifier pins down that actual behavior rather than the
paper's ideal.
"""

from __future__ import annotations

import numpy as np

from conftest import save_vector


VALUATION_DATE = "2026-01-02"
EPSILON = 1e-6


def _build_surface_points(maturities: list[float], strikes: list[float], sigma: float) -> list[dict]:
    # Flat implied vol surface — the current calibrator uses market.FlatVolatility
    # as σ₀ regardless of surface content, so flat-surface input keeps the
    # round-trip deterministic.
    return [
        {"maturity": t, "strike": k, "vol": sigma}
        for t in maturities
        for k in strikes
    ]


def _calibrated_tree(spot: float, sigma0: float, r: float, q: float, t: float,
                    steps: int) -> dict:
    dt = t / steps
    u = float(np.exp(sigma0 * np.sqrt(dt)))
    d = 1.0 / u
    growth = float(np.exp((r - q) * dt))
    p_crr = (growth - d) / (u - d)
    p = float(np.clip(p_crr, EPSILON, 1.0 - EPSILON))
    discount = float(np.exp(-r * dt))

    # Spot lattice.
    lattice: list[list[float]] = []
    for step in range(steps + 1):
        layer = [spot * (u ** (step - j)) * (d ** j) for j in range(step + 1)]
        lattice.append(layer)

    # AD forward induction.
    ad: list[list[float]] = [[1.0]]
    for step in range(steps):
        current = ad[step]
        nxt: list[float] = []
        for j in range(step + 2):
            v = 0.0
            if 0 < j <= step + 1 and (j - 1) <= step:
                v += current[j - 1] * (1.0 - p) * discount
            if j <= step:
                v += current[j] * p * discount
            nxt.append(v)
        ad.append(nxt)

    # Local vols: uniform σ₀ because node spacing is uniform in log-space.
    local_vols: list[list[float]] = []
    for step in range(steps):
        stepvols = []
        for j in range(step + 1):
            s_up = lattice[step + 1][j]
            s_dn = lattice[step + 1][j + 1]
            stepvols.append(float(np.log(s_up / s_dn) / (2.0 * np.sqrt(dt))))
        local_vols.append(stepvols)

    return {
        "u": u,
        "d": d,
        "p": p,
        "dt": dt,
        "spot_lattice": lattice,
        "arrow_debreu_prices": ad,
        "local_volatilities": local_vols,
    }


def _scenarios() -> list[dict]:
    out: list[dict] = []
    spot = 100.0
    strikes = [80.0, 90.0, 100.0, 110.0, 120.0]

    for days in (182, 365):
        t = days / 365.0
        for sigma0 in (0.15, 0.25):
            for r, q in ((0.04, 0.00), (0.05, 0.02)):
                for steps in (5, 10, 20):
                    maturities = [t]
                    surface_points = _build_surface_points(maturities, strikes, sigma0)
                    tree = _calibrated_tree(spot, sigma0, r, q, t, steps)
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
                        "expected": tree,
                    })
    return out


def generate() -> None:
    save_vector(
        "implied_tree",
        {
            "meta": {
                "reference": "Python replica of the CRR-backed implied-tree calibration as implemented in C#",
                "precision_tier": "PRECISION_NUMERIC",
                "note": "Current C# calibrators (Rubinstein + Derman-Kani) fall back to clamped CRR probabilities; the verifier pins down that actual behavior",
                "epsilon_clamp": EPSILON,
            },
            "scenarios": _scenarios(),
        },
    )


if __name__ == "__main__":
    generate()
    print("Wrote implied_tree.json")
