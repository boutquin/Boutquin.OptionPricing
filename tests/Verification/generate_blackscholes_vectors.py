"""
Reference Black-Scholes prices and Greeks using scipy.stats.norm as the
ground-truth normal CDF/PDF implementation.

Both BlackScholesPricer and BlackScholesGreeks follow Hull §19.12 "Extension
of formulas": one carry parameter q_eff = q + b (dividend yield + borrow
rate) is used symmetrically in d1, d2, every e^{-qT} discount factor, and
the q S N(d1) drift term in theta. Borrow therefore affects every Greek,
not just the price. The verifier exercises b ≠ 0 in the main grid and
asserts price AND Greeks at PRECISION_NUMERIC.

Convention notes (mirror the C# side):
  - Vega is quoted per 1 percentage-point move: vega = S e^{-q_eff T} phi(d1) sqrt(T) / 100.
  - Theta is daily (annual / 365).
  - Rho is quoted per 1 percentage-point move (derivative w.r.t. r only).
  - Year fractions use Actual/365Fixed: T = days / 365.
  - Valuation date is fixed at 2026-01-02; C# derives expiry as
    valuationDate.AddDays(days_to_expiry).
"""

from __future__ import annotations

import numpy as np
from scipy import stats

from conftest import save_vector


VALUATION_DATE = "2026-01-02"


def _d1_d2(s: float, k: float, r: float, q_eff: float, sigma: float, t: float) -> tuple[float, float]:
    sigma_sqrt_t = sigma * np.sqrt(t)
    d1 = (np.log(s / k) + (r - q_eff + 0.5 * sigma * sigma) * t) / sigma_sqrt_t
    d2 = d1 - sigma_sqrt_t
    return float(d1), float(d2)


def _bsm_price(s: float, k: float, r: float, q_eff: float, sigma: float, t: float, is_call: bool) -> float:
    d1, d2 = _d1_d2(s, k, r, q_eff, sigma, t)
    disc_spot = s * np.exp(-q_eff * t)
    disc_strike = k * np.exp(-r * t)
    if is_call:
        price = disc_spot * stats.norm.cdf(d1) - disc_strike * stats.norm.cdf(d2)
    else:
        price = disc_strike * stats.norm.cdf(-d2) - disc_spot * stats.norm.cdf(-d1)
    return float(price)


def _bsm_greeks(s: float, k: float, r: float, q_eff: float, sigma: float, t: float, is_call: bool) -> dict:
    d1, d2 = _d1_d2(s, k, r, q_eff, sigma, t)
    pdf = float(stats.norm.pdf(d1))
    sqrt_t = float(np.sqrt(t))
    disc_spot = float(np.exp(-q_eff * t))
    disc_strike = float(np.exp(-r * t))

    if is_call:
        delta = disc_spot * float(stats.norm.cdf(d1))
        theta_annual = (
            -s * disc_spot * pdf * sigma / (2.0 * sqrt_t)
            - r * k * disc_strike * float(stats.norm.cdf(d2))
            + q_eff * s * disc_spot * float(stats.norm.cdf(d1))
        )
        rho = k * t * disc_strike * float(stats.norm.cdf(d2)) / 100.0
    else:
        delta = disc_spot * (float(stats.norm.cdf(d1)) - 1.0)
        theta_annual = (
            -s * disc_spot * pdf * sigma / (2.0 * sqrt_t)
            + r * k * disc_strike * float(stats.norm.cdf(-d2))
            - q_eff * s * disc_spot * float(stats.norm.cdf(-d1))
        )
        rho = -k * t * disc_strike * float(stats.norm.cdf(-d2)) / 100.0

    gamma = disc_spot * pdf / (s * sigma * sqrt_t)
    vega = s * disc_spot * pdf * sqrt_t / 100.0
    theta = theta_annual / 365.0

    return {
        "delta": float(delta),
        "gamma": float(gamma),
        "vega": float(vega),
        "theta": float(theta),
        "rho": float(rho),
    }


def _scenario(days: int, strike: float, sigma: float, r: float, q: float, b: float,
              is_call: bool, spot: float = 100.0) -> dict:
    t = days / 365.0
    q_eff = q + b
    price = _bsm_price(spot, strike, r, q_eff, sigma, t, is_call)
    greeks = _bsm_greeks(spot, strike, r, q_eff, sigma, t, is_call)
    return {
        "valuation_date": VALUATION_DATE,
        "days_to_expiry": days,
        "T": t,
        "spot": spot,
        "strike": strike,
        "sigma": sigma,
        "r": r,
        "q": q,
        "b": b,
        "call_put": "Call" if is_call else "Put",
        "price": price,
        **greeks,
    }


def _build_scenarios() -> list[dict]:
    scenarios: list[dict] = []
    # Grid covers ITM/ATM/OTM across short/medium/long maturities. Day counts
    # are integer so Actual/365Fixed year fractions match T exactly on both
    # sides. Borrow rates are exercised in the main grid per Hull §19.12 —
    # b now flows symmetrically through the Greeks as well as the price.
    for days in (30, 91, 182, 365, 730):
        for strike in (80.0, 95.0, 100.0, 105.0, 125.0):
            for sigma in (0.10, 0.20, 0.40):
                for r, q, b in (
                    (0.03, 0.00, 0.000),
                    (0.05, 0.02, 0.000),
                    (0.05, 0.02, 0.015),  # non-zero borrow, now symmetric
                    (0.04, 0.01, 0.020),
                    (0.00, 0.00, 0.000),
                ):
                    for is_call in (True, False):
                        scenarios.append(_scenario(days, strike, sigma, r, q, b, is_call))
    return scenarios


def _build_parity_vectors() -> list[dict]:
    # Put-call parity with unified carry q_eff = q + b:
    #   C - P = S e^{-q_eff T} - K e^{-r T}.
    out: list[dict] = []
    spot = 100.0
    for days in (91, 182, 365):
        for strike in (90.0, 100.0, 110.0):
            for r, q, b in ((0.04, 0.015, 0.000), (0.04, 0.01, 0.015)):
                sigma = 0.25
                t = days / 365.0
                q_eff = q + b
                call = _bsm_price(spot, strike, r, q_eff, sigma, t, True)
                put = _bsm_price(spot, strike, r, q_eff, sigma, t, False)
                parity_diff = spot * np.exp(-q_eff * t) - strike * np.exp(-r * t)
                out.append({
                    "valuation_date": VALUATION_DATE,
                    "days_to_expiry": days,
                    "T": t,
                    "spot": spot,
                    "strike": strike,
                    "sigma": sigma,
                    "r": r,
                    "q": q,
                    "b": b,
                    "call_price": call,
                    "put_price": put,
                    "parity_rhs": float(parity_diff),
                })
    return out


def generate() -> None:
    save_vector(
        "blackscholes",
        {
            "meta": {
                "reference": "scipy.stats.norm closed-form BSM",
                "precision_tier": "PRECISION_NUMERIC",
                "convention": "Hull §19.12 — one carry parameter q_eff = q + b, symmetric across price and all Greeks",
                "greeks_scaling": {
                    "vega": "per 1 percentage-point of sigma (divided by 100)",
                    "theta": "per calendar day (annual / 365)",
                    "rho": "per 1 percentage-point of r (divided by 100)",
                },
                "day_count": "Actual/365Fixed",
            },
            "scenarios": _build_scenarios(),
            "parity": _build_parity_vectors(),
        },
    )


if __name__ == "__main__":
    generate()
    print("Wrote blackscholes.json")
