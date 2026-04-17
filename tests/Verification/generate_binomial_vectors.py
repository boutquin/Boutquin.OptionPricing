"""
Reference binomial tree prices for CRR and Jarrow-Rudd parameterizations,
European and American exercise.

The Python reference reproduces the C# algorithm verbatim:
  * CRR: u = exp(sigma sqrt(dt)), d = 1/u, p = (exp((r-q) dt) - d) / (u - d).
  * Jarrow-Rudd: drift = (r - q - sigma^2 / 2) dt,
                 u = exp(drift + sigma sqrt(dt)), d = exp(drift - sigma sqrt(dt)),
                 p = 0.5.
  * Backward induction uses discount = exp(-r dt); European takes continuation,
    American takes max(continuation, intrinsic).
  * Carry q in the tree builders is (dividend - borrow) per
    TreeModelInput.DividendYield() - TreeModelInput.BorrowRate().

Because the algorithm is identical and double arithmetic, the Python and
C# trees should agree to within PRECISION_NUMERIC modulo floating-point
summation order. A separate section also records the BSM analytic price
so the convergence tests in C# can assert |tree - analytic| shrinks with N.
"""

from __future__ import annotations

import numpy as np
from scipy import stats

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


def _crr_params(r: float, q: float, sigma: float, dt: float) -> tuple[float, float, float]:
    u = float(np.exp(sigma * np.sqrt(dt)))
    d = 1.0 / u
    p = (float(np.exp((r - q) * dt)) - d) / (u - d)
    return u, d, p


def _jr_params(r: float, q: float, sigma: float, dt: float) -> tuple[float, float, float]:
    drift = (r - q - 0.5 * sigma * sigma) * dt
    vol_sqrt_dt = sigma * float(np.sqrt(dt))
    u = float(np.exp(drift + vol_sqrt_dt))
    d = float(np.exp(drift - vol_sqrt_dt))
    return u, d, 0.5


def _price_tree(s: float, k: float, r: float, q_tree: float, sigma: float, t: float,
                steps: int, is_call: bool, is_american: bool, scheme: str) -> float:
    """Price option on a recombining binomial tree.

    q_tree is the tree carry (dividend_yield - borrow_rate) to match the C#
    TreeBuilder convention: q = input.DividendYield() - input.BorrowRate().
    """
    dt = t / steps
    if scheme == "crr":
        u, d, p = _crr_params(r, q_tree, sigma, dt)
    elif scheme == "jr":
        u, d, p = _jr_params(r, q_tree, sigma, dt)
    else:
        raise ValueError(f"Unknown scheme {scheme!r}")

    # Terminal spots: s0 * u^(N-j) * d^j for j in 0..N.
    s0 = s
    terminal = np.array([s0 * (u ** (steps - j)) * (d ** j) for j in range(steps + 1)], dtype=float)
    if is_call:
        values = np.maximum(terminal - k, 0.0)
    else:
        values = np.maximum(k - terminal, 0.0)

    disc = float(np.exp(-r * dt))
    for step in range(steps - 1, -1, -1):
        values = disc * (p * values[:-1] + (1.0 - p) * values[1:])
        if is_american:
            spots = np.array([s0 * (u ** (step - j)) * (d ** j) for j in range(step + 1)], dtype=float)
            if is_call:
                intrinsic = np.maximum(spots - k, 0.0)
            else:
                intrinsic = np.maximum(k - spots, 0.0)
            values = np.maximum(values, intrinsic)

    return float(values[0])


def _scenarios() -> list[dict]:
    # Smaller grid than BSM: tree pricing is O(N^2), so generating many
    # reference vectors for N=500 is the dominant cost of regenerating the
    # vectors. Still ample coverage of moneyness/maturity/vol regimes.
    out: list[dict] = []
    spot = 100.0
    for days in (91, 182, 365):
        t = days / 365.0
        for strike in (90.0, 100.0, 110.0):
            for sigma in (0.15, 0.30):
                for r, q in ((0.04, 0.00), (0.06, 0.02)):
                    q_tree = q  # borrow is zero in these scenarios
                    for scheme in ("crr", "jr"):
                        for is_call in (True, False):
                            for steps in (50, 200):
                                euro_price = _price_tree(
                                    spot, strike, r, q_tree, sigma, t, steps,
                                    is_call=is_call, is_american=False, scheme=scheme,
                                )
                                amer_price = _price_tree(
                                    spot, strike, r, q_tree, sigma, t, steps,
                                    is_call=is_call, is_american=True, scheme=scheme,
                                )
                                out.append({
                                    "valuation_date": VALUATION_DATE,
                                    "days_to_expiry": days,
                                    "T": t,
                                    "spot": spot,
                                    "strike": strike,
                                    "sigma": sigma,
                                    "r": r,
                                    "q": q,
                                    "scheme": scheme,
                                    "steps": steps,
                                    "call_put": "Call" if is_call else "Put",
                                    "european_price": euro_price,
                                    "american_price": amer_price,
                                })
    return out


def _convergence() -> list[dict]:
    # Fine-N tree prices paired with the BSM analytic so C# tests can assert
    # |tree - analytic| <= tolerance. Only European (American has no
    # closed-form). One representative contract per scheme.
    out: list[dict] = []
    spot = 100.0
    for days in (91, 182, 365):
        t = days / 365.0
        for strike in (95.0, 105.0):
            sigma = 0.25
            r, q = 0.04, 0.01
            for is_call in (True, False):
                analytic = _bsm_price(spot, strike, r, q, sigma, t, is_call)
                for scheme in ("crr", "jr"):
                    for steps in (100, 400):
                        tree_price = _price_tree(
                            spot, strike, r, q, sigma, t, steps,
                            is_call=is_call, is_american=False, scheme=scheme,
                        )
                        out.append({
                            "valuation_date": VALUATION_DATE,
                            "days_to_expiry": days,
                            "T": t,
                            "spot": spot,
                            "strike": strike,
                            "sigma": sigma,
                            "r": r,
                            "q": q,
                            "scheme": scheme,
                            "steps": steps,
                            "call_put": "Call" if is_call else "Put",
                            "analytic_price": analytic,
                            "tree_price": tree_price,
                        })
    return out


def _arrow_debreu() -> list[dict]:
    # Arrow-Debreu terminal state prices on a CRR tree:
    #   AD(j) = e^{-r N dt} * C(N, j) * p^{N-j} * (1-p)^j
    # These must sum to the discount factor e^{-r T}. Small step counts keep
    # the JSON compact while covering a range of moneyness and time.
    out: list[dict] = []
    spot = 100.0
    for days in (91, 365):
        t = days / 365.0
        for sigma in (0.15, 0.30):
            r, q = 0.05, 0.02
            for steps in (8, 16, 32):
                dt = t / steps
                u, d, p = _crr_params(r, q, sigma, dt)
                discount = float(np.exp(-r * steps * dt))
                # AD prices ordered state 0 = most up moves (N-j=steps, j=0).
                ad = []
                # Use scipy.special.comb for exact binomial coefficients.
                from scipy.special import comb
                for j in range(steps + 1):
                    c = float(comb(steps, j, exact=False))
                    ad.append(discount * c * (p ** (steps - j)) * ((1 - p) ** j))
                out.append({
                    "valuation_date": VALUATION_DATE,
                    "days_to_expiry": days,
                    "T": t,
                    "spot": spot,
                    "sigma": sigma,
                    "r": r,
                    "q": q,
                    "steps": steps,
                    "arrow_debreu_prices": ad,
                    "ad_sum": float(sum(ad)),
                    "expected_ad_sum": discount,
                })
    return out


def generate() -> None:
    save_vector(
        "binomial",
        {
            "meta": {
                "reference": "Python implementation of identical CRR / Jarrow-Rudd algorithm",
                "scenarios_precision_tier": "PRECISION_NUMERIC (identical algorithm, double arithmetic)",
                "convergence_precision": "|tree - analytic| shrinks as O(1/N); test uses empirical bounds",
                "arrow_debreu_precision": "PRECISION_NUMERIC (identical algorithm)",
                "day_count": "Actual/365Fixed",
            },
            "scenarios": _scenarios(),
            "convergence": _convergence(),
            "arrow_debreu": _arrow_debreu(),
        },
    )


if __name__ == "__main__":
    generate()
    print("Wrote binomial.json")
