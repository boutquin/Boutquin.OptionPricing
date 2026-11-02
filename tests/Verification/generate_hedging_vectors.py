"""
Reference vectors for DeltaHedgeSimulator.

The C# simulator walks a pre-specified spot path (not randomly generated),
rebalances the delta hedge at each step using BlackScholesGreeks, grows cash
at e^{r·dt} between rebalances, and reports the terminal portfolio value and
hedging error. Because the path is an explicit input (not drawn from an
internal RNG), the simulation is deterministic and Python replicates the
identical algorithm.

Each scenario stores:
  * The valuation date, option (European call/put, strike, expiry in days).
  * The initial market parameters (spot, sigma, r, q). The fixtures use zero
    borrow; separate executable controls exercise model-equivalent borrow yield.
  * The GBM-generated path (fixed seed, fixed dt = T/N, N steps).
  * The expected start price, delta sequence, final portfolio value,
    option payoff, and hedging error.

Rolling forward: the C# simulator rebuilds its FlatDiscountCurve instances
at each stepDate so that the implied continuous rate stays invariant under
time advancement (see OptionMarketData.RolledTo). The Python reference
uses the same constant r and q across all rebalances, matching that semantics.
"""

from __future__ import annotations

import numpy as np
from scipy import stats

from conftest import save_vector


VALUATION_DATE = "2026-01-02"


def _bsm_price(s: float, k: float, r: float, q: float, sigma: float, t: float, is_call: bool) -> float:
    if t <= 0:
        return max(s - k, 0.0) if is_call else max(k - s, 0.0)
    sigma_sqrt_t = sigma * np.sqrt(t)
    d1 = (np.log(s / k) + (r - q + 0.5 * sigma * sigma) * t) / sigma_sqrt_t
    d2 = d1 - sigma_sqrt_t
    disc_spot = s * np.exp(-q * t)
    disc_strike = k * np.exp(-r * t)
    if is_call:
        return float(disc_spot * stats.norm.cdf(d1) - disc_strike * stats.norm.cdf(d2))
    return float(disc_strike * stats.norm.cdf(-d2) - disc_spot * stats.norm.cdf(-d1))


def _bsm_delta(s: float, k: float, r: float, q: float, sigma: float, t: float, is_call: bool) -> float:
    if t <= 0:
        return 0.0
    sigma_sqrt_t = sigma * np.sqrt(t)
    d1 = (np.log(s / k) + (r - q + 0.5 * sigma * sigma) * t) / sigma_sqrt_t
    disc_spot = float(np.exp(-q * t))
    if is_call:
        return disc_spot * float(stats.norm.cdf(d1))
    return disc_spot * (float(stats.norm.cdf(d1)) - 1.0)


def _simulate(spots: list[float], k: float, r: float, q: float, sigma: float,
              total_t: float, total_days: int, is_call: bool) -> dict:
    """Replicate DeltaHedgeSimulator.Simulate verbatim.

    The C# simulator calls ``market.InvertYearFraction(i*dt)`` which, under
    Actual/365Fixed, returns the valuation_date + round(i*dt*365) days. We
    emulate that day-rounded step date so remaining T lines up with what the
    C# Greeks call actually sees.
    """
    n = len(spots) - 1
    dt = total_t / n
    growth_per_step = float(np.exp(r * dt))

    start_price = _bsm_price(spots[0], k, r, q, sigma, total_t, is_call)
    deltas = [_bsm_delta(spots[0], k, r, q, sigma, total_t, is_call)]
    current_delta = deltas[0]
    cash = start_price - current_delta * spots[0]

    for i in range(1, n):
        cash *= growth_per_step
        # Continuous model yield is reinvested in held shares before rebalancing.
        current_delta *= float(np.exp(q * dt))
        # Emulate InvertYearFraction + the C# clamp "if stepDate >= expiryDate,
        # stepDate = expiryDate.AddDays(-1)".
        step_days = round(i * dt * 365)
        if step_days >= total_days:
            step_days = total_days - 1
        remaining_t = (total_days - step_days) / 365.0
        # C# rolls the FlatDiscountCurve forward to stepDate (see
        # OptionMarketData.RolledTo), so the implied continuous rate is
        # invariant in time — we use the same r and q at every rebalance.
        new_delta = _bsm_delta(spots[i], k, r, q, sigma, remaining_t, is_call)
        cash -= (new_delta - current_delta) * spots[i]
        current_delta = new_delta
        deltas.append(current_delta)

    cash *= growth_per_step
    current_delta *= float(np.exp(q * dt))
    terminal_spot = spots[-1]
    final_portfolio = current_delta * terminal_spot + cash
    payoff = max(terminal_spot - k, 0.0) if is_call else max(k - terminal_spot, 0.0)
    return {
        "start_price": start_price,
        "deltas": deltas,
        "final_portfolio_value": final_portfolio,
        "option_payoff": payoff,
        "hedging_error": final_portfolio - payoff,
    }


def _generate_path(s0: float, mu: float, sigma: float, total_t: float, n: int, seed: int) -> list[float]:
    # Deterministic GBM path — this is the reference path both C# and Python
    # consume identically (the C# simulator does not draw its own randoms).
    rng = np.random.default_rng(seed)
    dt = total_t / n
    drift = (mu - 0.5 * sigma * sigma) * dt
    vol_sqrt_dt = sigma * np.sqrt(dt)
    z = rng.standard_normal(n)
    log_returns = drift + vol_sqrt_dt * z
    log_spots = np.log(s0) + np.concatenate([[0.0], np.cumsum(log_returns)])
    return [float(x) for x in np.exp(log_spots)]


def _scenarios() -> list[dict]:
    out: list[dict] = []
    s0 = 100.0
    configs = [
        # (days, n_steps, strike, sigma, r, q, is_call, seed)
        (91, 21, 100.0, 0.20, 0.04, 0.00, True, 20260414),
        (91, 21, 100.0, 0.20, 0.04, 0.00, False, 20260414),
        (182, 26, 95.0, 0.25, 0.05, 0.02, True, 7),
        (182, 26, 105.0, 0.25, 0.05, 0.02, False, 42),
        (365, 52, 100.0, 0.30, 0.03, 0.00, True, 123),
        (365, 52, 110.0, 0.18, 0.04, 0.01, False, 987),
    ]

    for days, n, strike, sigma, r, q, is_call, seed in configs:
        t = days / 365.0
        path = _generate_path(s0, mu=r - q, sigma=sigma, total_t=t, n=n, seed=seed)
        result = _simulate(path, strike, r, q, sigma, t, days, is_call)
        out.append({
            "valuation_date": VALUATION_DATE,
            "days_to_expiry": days,
            "steps_per_path": n,
            "T": t,
            "spot0": s0,
            "strike": strike,
            "sigma": sigma,
            "r": r,
            "q": q,
            "call_put": "Call" if is_call else "Put",
            "path_seed": seed,
            "path_spots": path,
            "expected_start_price": result["start_price"],
            "expected_deltas": result["deltas"],
            "expected_final_portfolio": result["final_portfolio_value"],
            "expected_option_payoff": result["option_payoff"],
            "expected_hedging_error": result["hedging_error"],
        })
    return out


def generate() -> None:
    save_vector(
        "hedging",
        {
            "meta": {
                "reference": "Python replication of DeltaHedgeSimulator's path-walking rebalance loop",
                "precision_tier": "PRECISION_NUMERIC (identical algorithm, pre-specified path)",
                "step_date_note": "Each scenario picks T = N·(days_per_step)/365 so InvertYearFraction lands on whole days",
                "day_count": "Actual/365Fixed",
            },
            "scenarios": _scenarios(),
        },
    )


if __name__ == "__main__":
    generate()
    print("Wrote hedging.json")
