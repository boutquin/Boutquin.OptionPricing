"""
Reference vectors for the Exotics.Barriers module.

Covers:
  * BarrierParity — algebraic identity Vanilla = KnockIn + KnockOut.
  * BarrierMonitoringAdjustments — Broadie-Glasserman-Kou continuity
    correction H_adj = H · exp(±β · σ · √(T/m)) with β = 0.5826.
  * BarrierTreePricer — Python replicates the identical backward-induction
    algorithm (zero continuation at breached nodes, parity for knock-in).
    Matches at PRECISION_NUMERIC.

SmileAwareBarrierPricer uses the same backward induction on a calibrated
ImpliedTreeResult's tree. Because both implied-tree calibrators in the C#
codebase produce a CRR-structured lattice with clamped CRR probabilities,
SmileAwareBarrierPricer on that output is mathematically equivalent to
BarrierTreePricer on a CRR tree with the same (u, d, p) — so we cover it
implicitly through BarrierTreePricer verification. A dedicated
SmileAware-on-calibrated-tree scenario would require running the calibrator
on the Python side, which the implied-tree verifier handles separately.
"""

from __future__ import annotations

import numpy as np

from conftest import save_vector


VALUATION_DATE = "2026-01-02"
BETA_BGK = 0.5826


def _crr_params(r: float, q: float, sigma: float, dt: float) -> tuple[float, float, float]:
    u = float(np.exp(sigma * np.sqrt(dt)))
    d = 1.0 / u
    p = (float(np.exp((r - q) * dt)) - d) / (u - d)
    return u, d, p


def _barrier_breached(spot: float, level: float, direction: str) -> bool:
    if direction == "Up":
        return spot >= level
    return spot <= level


def _payoff(spot: float, strike: float, is_call: bool) -> float:
    if is_call:
        return max(spot - strike, 0.0)
    return max(strike - spot, 0.0)


def _price_knock_out_crr(spot: float, strike: float, barrier: float, direction: str,
                         r: float, q: float, sigma: float, t: float, steps: int,
                         is_call: bool) -> float:
    dt = t / steps
    u, d, p = _crr_params(r, q, sigma, dt)
    discount = float(np.exp(-r * dt))

    terminal = np.array([spot * (u ** (steps - j)) * (d ** j) for j in range(steps + 1)], dtype=float)
    values = np.array([
        0.0 if _barrier_breached(s, barrier, direction) else _payoff(s, strike, is_call)
        for s in terminal
    ], dtype=float)

    for step in range(steps - 1, -1, -1):
        spots = np.array([spot * (u ** (step - j)) * (d ** j) for j in range(step + 1)], dtype=float)
        next_vals = np.empty(step + 1, dtype=float)
        for j in range(step + 1):
            if _barrier_breached(spots[j], barrier, direction):
                next_vals[j] = 0.0
            else:
                next_vals[j] = discount * (p * values[j] + (1.0 - p) * values[j + 1])
        values = next_vals

    return max(float(values[0]), 0.0)


def _price_vanilla_crr(spot: float, strike: float, r: float, q: float, sigma: float,
                      t: float, steps: int, is_call: bool) -> float:
    dt = t / steps
    u, d, p = _crr_params(r, q, sigma, dt)
    discount = float(np.exp(-r * dt))

    terminal = np.array([spot * (u ** (steps - j)) * (d ** j) for j in range(steps + 1)], dtype=float)
    values = np.array([_payoff(s, strike, is_call) for s in terminal], dtype=float)

    for step in range(steps - 1, -1, -1):
        values = discount * (p * values[:-1] + (1.0 - p) * values[1:])

    return float(values[0])


def _parity_scenarios() -> list[dict]:
    # Vanilla = KnockIn + KnockOut algebraic identity
    out: list[dict] = []
    for vanilla, ko in ((5.0, 3.0), (10.0, 0.0), (7.5, 7.5), (1.25, 0.75)):
        out.append({
            "vanilla_price": vanilla,
            "knock_out_price": ko,
            "implied_knock_in": vanilla - ko,
        })
        out.append({
            "vanilla_price": vanilla,
            "knock_in_price": vanilla - ko,
            "implied_knock_out": ko,
        })
    return out


def _monitoring_scenarios() -> list[dict]:
    out: list[dict] = []
    for barrier in (90.0, 100.0, 120.0):
        for direction in ("Up", "Down"):
            for sigma in (0.15, 0.30):
                for days in (91, 182, 365):
                    t = days / 365.0
                    for m in (1, 5, 21, 63, 252):
                        sign = 1.0 if direction == "Up" else -1.0
                        exponent = sign * BETA_BGK * sigma * np.sqrt(t / m)
                        adjusted = barrier * float(np.exp(exponent))
                        out.append({
                            "barrier_level": barrier,
                            "direction": direction,
                            "sigma": sigma,
                            "T": t,
                            "monitoring_dates": m,
                            "adjusted_barrier": adjusted,
                        })
    return out


def _tree_pricer_scenarios() -> list[dict]:
    out: list[dict] = []
    spot = 100.0
    for days in (91, 182, 365):
        t = days / 365.0
        for strike in (95.0, 100.0, 105.0):
            for sigma in (0.20, 0.30):
                r, q = 0.04, 0.00
                # Barriers deep enough not to be breached at spot but reachable
                # along plausible paths.
                for direction, level in (("Up", 120.0), ("Up", 130.0), ("Down", 85.0), ("Down", 75.0)):
                    for knock in ("Out", "In"):
                        for is_call in (True, False):
                            for steps in (50, 200):
                                vanilla = _price_vanilla_crr(spot, strike, r, q, sigma, t, steps, is_call)
                                knock_out = _price_knock_out_crr(
                                    spot, strike, level, direction, r, q, sigma, t, steps, is_call,
                                )
                                price = knock_out if knock == "Out" else max(vanilla - knock_out, 0.0)
                                out.append({
                                    "valuation_date": VALUATION_DATE,
                                    "days_to_expiry": days,
                                    "T": t,
                                    "spot": spot,
                                    "strike": strike,
                                    "sigma": sigma,
                                    "r": r,
                                    "q": q,
                                    "barrier_level": level,
                                    "direction": direction,
                                    "knock_type": knock,
                                    "call_put": "Call" if is_call else "Put",
                                    "steps": steps,
                                    "price": price,
                                })
    return out


def _tree_pricer_breached_scenarios() -> list[dict]:
    # Spot-is-breached edge cases the pricer handles specially.
    out: list[dict] = []
    # Up barrier breached at spot — knock-out = 0, knock-in = vanilla.
    spot = 120.0
    barrier = 100.0
    strike = 100.0
    r, q = 0.04, 0.00
    sigma = 0.20
    days = 182
    t = days / 365.0
    steps = 100
    vanilla_call = _price_vanilla_crr(spot, strike, r, q, sigma, t, steps, True)
    out.append({
        "valuation_date": VALUATION_DATE,
        "days_to_expiry": days,
        "T": t,
        "spot": spot,
        "strike": strike,
        "sigma": sigma,
        "r": r,
        "q": q,
        "barrier_level": barrier,
        "direction": "Up",
        "knock_type": "Out",
        "call_put": "Call",
        "steps": steps,
        "expected_price": 0.0,
        "case": "knock_out_breached",
    })
    out.append({
        "valuation_date": VALUATION_DATE,
        "days_to_expiry": days,
        "T": t,
        "spot": spot,
        "strike": strike,
        "sigma": sigma,
        "r": r,
        "q": q,
        "barrier_level": barrier,
        "direction": "Up",
        "knock_type": "In",
        "call_put": "Call",
        "steps": steps,
        "expected_price": vanilla_call,
        "case": "knock_in_breached_equals_vanilla",
    })
    return out


def generate() -> None:
    save_vector(
        "barrier",
        {
            "meta": {
                "reference": "Python reimplementation of BarrierTreePricer algorithm + BGK monitoring + parity algebra",
                "precision_tier": "PRECISION_NUMERIC (identical algorithm) / PRECISION_EXACT (algebra)",
                "beta_bgk": BETA_BGK,
                "day_count": "Actual/365Fixed",
            },
            "parity": _parity_scenarios(),
            "monitoring": _monitoring_scenarios(),
            "tree_pricer": _tree_pricer_scenarios(),
            "tree_pricer_breached": _tree_pricer_breached_scenarios(),
        },
    )


if __name__ == "__main__":
    generate()
    print("Wrote barrier.json")
