"""
Reference values for PutCallParity and RiskNeutralValuation.

Both are pure-algebra utilities:
  * PutCallParity.ImpliedCallPrice  = putPrice + S * DF_div - K * DF_rf
  * PutCallParity.ImpliedPutPrice   = callPrice - S * DF_div + K * DF_rf
  * RiskNeutralValuation.ForwardPrice = S * DF_div * DF_borrow / DF_rf
    (borrow cost reduces the forward, consistent with the pricer's effectiveQ = q + b)

No numerical approximation beyond double arithmetic → PRECISION_EXACT.
"""

from __future__ import annotations

import numpy as np

from conftest import save_vector


VALUATION_DATE = "2026-01-02"


def _discount(rate: float, t: float) -> float:
    return float(np.exp(-rate * t))


def _implied_call(put: float, s: float, k: float, df_rf: float, df_div: float) -> float:
    return put + s * df_div - k * df_rf


def _implied_put(call: float, s: float, k: float, df_rf: float, df_div: float) -> float:
    return call - s * df_div + k * df_rf


def _forward(s: float, df_rf: float, df_div: float, df_borrow: float) -> float:
    return s * df_div * df_borrow / df_rf


def _parity_scenarios() -> list[dict]:
    out: list[dict] = []
    spot = 100.0
    for days in (30, 91, 182, 365, 730):
        t = days / 365.0
        for strike in (80.0, 95.0, 100.0, 110.0, 125.0):
            for r, q in ((0.00, 0.00), (0.03, 0.00), (0.05, 0.02), (0.04, 0.015)):
                df_rf = _discount(r, t)
                df_div = _discount(q, t)
                # Use arbitrary reference call/put pairs (deterministic, not BSM —
                # we're testing algebra, not pricing). Any pair of decimals works:
                # the utility is input-agnostic.
                for call_in, put_in in ((5.0, 3.0), (10.0, 0.5), (0.0, 0.0), (20.0, 20.0)):
                    implied_call = _implied_call(put_in, spot, strike, df_rf, df_div)
                    implied_put = _implied_put(call_in, spot, strike, df_rf, df_div)
                    out.append({
                        "valuation_date": VALUATION_DATE,
                        "days_to_expiry": days,
                        "T": t,
                        "spot": spot,
                        "strike": strike,
                        "r": r,
                        "q": q,
                        "df_rf": df_rf,
                        "df_div": df_div,
                        "observed_call": call_in,
                        "observed_put": put_in,
                        "implied_call_from_put": implied_call,
                        "implied_put_from_call": implied_put,
                    })
    return out


def _forward_scenarios() -> list[dict]:
    out: list[dict] = []
    for spot in (50.0, 100.0, 150.0):
        for days in (30, 182, 365, 730):
            t = days / 365.0
            for r, q, b in (
                (0.00, 0.00, 0.000),
                (0.04, 0.02, 0.000),
                (0.05, 0.015, 0.020),
                (0.06, 0.00, 0.010),
            ):
                df_rf = _discount(r, t)
                df_div = _discount(q, t)
                df_borrow = _discount(b, t)
                out.append({
                    "valuation_date": VALUATION_DATE,
                    "days_to_expiry": days,
                    "T": t,
                    "spot": spot,
                    "r": r,
                    "q": q,
                    "b": b,
                    "df_rf": df_rf,
                    "df_div": df_div,
                    "df_borrow": df_borrow,
                    "forward_price": _forward(spot, df_rf, df_div, df_borrow),
                })
    return out


def generate() -> None:
    save_vector(
        "parity",
        {
            "meta": {
                "reference": "pure algebra — parity identities and cost-of-carry forward",
                "precision_tier": "PRECISION_EXACT",
                "day_count": "Actual/365Fixed",
            },
            "put_call_parity": _parity_scenarios(),
            "forward_prices": _forward_scenarios(),
        },
    )


if __name__ == "__main__":
    generate()
    print("Wrote parity.json")
