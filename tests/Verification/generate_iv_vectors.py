"""
Reference implied-volatility round-trip vectors.

For each (s, k, r, q, sigma, t, call/put) we:
  1. Compute the BSM price at the true sigma (same formula as
     generate_blackscholes_vectors.py).
  2. Solve for the implied vol from that price using scipy.optimize.brentq
     on the BSM minus-observed-price objective — this is an independent
     implementation of the Brent root-finder from the C# solver's inputs
     (C# uses Boutquin.Numerics BrentSolver; Python uses scipy's FORTRAN
     port of Brent 1973).
  3. Both implementations must recover the true sigma to PRECISION_NUMERIC.

The solver is restricted to European options; every scenario uses a vanilla
European call or put.
"""

from __future__ import annotations

import numpy as np
from scipy import optimize, stats

from conftest import save_vector


VALUATION_DATE = "2026-01-02"


def _bsm_price(s: float, k: float, r: float, q: float, sigma: float, t: float, is_call: bool) -> float:
    sigma_sqrt_t = sigma * np.sqrt(t)
    d1 = (np.log(s / k) + (r - q + 0.5 * sigma * sigma) * t) / sigma_sqrt_t
    d2 = d1 - sigma_sqrt_t
    disc_spot = s * np.exp(-q * t)
    disc_strike = k * np.exp(-r * t)
    if is_call:
        return float(disc_spot * stats.norm.cdf(d1) - disc_strike * stats.norm.cdf(d2))
    return float(disc_strike * stats.norm.cdf(-d2) - disc_spot * stats.norm.cdf(-d1))


def _solve_iv(market_price: float, s: float, k: float, r: float, q: float, t: float,
              is_call: bool) -> float:
    def obj(sigma: float) -> float:
        return _bsm_price(s, k, r, q, sigma, t, is_call) - market_price

    # Match C# ImpliedVolatilitySolver bracket: [1e-6, 5.0].
    try:
        return float(optimize.brentq(obj, 1e-6, 5.0, xtol=1e-10, rtol=1e-12))
    except ValueError:
        # Bracket doesn't contain a sign change — indicates an infeasible
        # market price. Return NaN so the C# test can assert the same.
        return float("nan")


def _time_value(s: float, k: float, r: float, q: float, price: float, is_call: bool, t: float) -> float:
    # Intrinsic of a European option is the discounted expected payoff assuming
    # zero vol — for our BSM with dividends, PV intrinsic is max(Fwd - K, 0) * DF
    # (call) or max(K - Fwd, 0) * DF (put). Time value is price - PV intrinsic.
    fwd = s * float(np.exp((r - q) * t))
    df = float(np.exp(-r * t))
    pv_intrinsic = df * (max(fwd - k, 0.0) if is_call else max(k - fwd, 0.0))
    return price - pv_intrinsic


def _scenarios() -> list[dict]:
    out: list[dict] = []
    spot = 100.0
    for days in (30, 91, 182, 365):
        t = days / 365.0
        for strike in (80.0, 95.0, 100.0, 105.0, 125.0):
            for true_sigma in (0.10, 0.20, 0.35, 0.60):
                for r, q in ((0.03, 0.00), (0.05, 0.02), (0.00, 0.00)):
                    for is_call in (True, False):
                        price = _bsm_price(spot, strike, r, q, true_sigma, t, is_call)
                        # Filter cases where the solver has insufficient vega to
                        # recover σ to PRECISION_NUMERIC. Heuristic: require the
                        # option to carry at least 1 cent of time value. This drops
                        # deep-ITM / deep-OTM short-dated scenarios whose price is
                        # indistinguishable from intrinsic and whose σ is therefore
                        # numerically indeterminate — the C# BrentSolver returns
                        # the bracket lower bound (1e-6) in those cases, which is
                        # behavior by design, not a regression.
                        tv = _time_value(spot, strike, r, q, price, is_call, t)
                        if tv < 1e-2:
                            continue
                        recovered = _solve_iv(price, spot, strike, r, q, t, is_call)
                        out.append({
                            "valuation_date": VALUATION_DATE,
                            "days_to_expiry": days,
                            "T": t,
                            "spot": spot,
                            "strike": strike,
                            "r": r,
                            "q": q,
                            "call_put": "Call" if is_call else "Put",
                            "true_sigma": true_sigma,
                            "time_value": tv,
                            "observed_price": price,
                            "recovered_sigma": recovered,
                        })
    return out


def generate() -> None:
    save_vector(
        "implied_volatility",
        {
            "meta": {
                "reference": "scipy.optimize.brentq on scipy.stats.norm BSM",
                "precision_tier": "PRECISION_NUMERIC — both solvers use Brent with tight tol",
                "bracket": "[1e-6, 5.0] — matches C# ImpliedVolatilitySolver",
                "solver_tolerance": "1e-8 (C# default), Python uses xtol=1e-10 rtol=1e-12",
                "day_count": "Actual/365Fixed",
            },
            "scenarios": _scenarios(),
        },
    )


if __name__ == "__main__":
    generate()
    print("Wrote implied_volatility.json")
