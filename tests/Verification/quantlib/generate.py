"""Execute pinned QuantLib engines; never import or copy OptionPricing algorithms."""
import argparse
import hashlib
import itertools
import json
from pathlib import Path
import QuantLib as ql

assert ql.__version__ == '1.40', ql.__version__
valuation = ql.Date(9, 4, 2026)
ql.Settings.instance().evaluationDate = valuation
day_count = ql.Actual365Fixed()
rows = []
for spot, strike, days, sigma, rate, carry in itertools.product(
        (80., 100., 120.), (90., 110.), (7, 91, 365, 1095), (.08, .20, .55), (-.01, .04), ((0., 0.), (.015, .025))):
    dividend, borrow = carry
    process = ql.BlackScholesMertonProcess(
        ql.QuoteHandle(ql.SimpleQuote(spot)),
        ql.YieldTermStructureHandle(ql.FlatForward(valuation, dividend + borrow, day_count)),
        ql.YieldTermStructureHandle(ql.FlatForward(valuation, rate, day_count)),
        ql.BlackVolTermStructureHandle(ql.BlackConstantVol(valuation, ql.NullCalendar(), sigma, day_count)))
    for kind, side in [('Call', ql.Option.Call), ('Put', ql.Option.Put)]:
        option = ql.VanillaOption(ql.PlainVanillaPayoff(side, strike), ql.EuropeanExercise(valuation + days))
        option.setPricingEngine(ql.AnalyticEuropeanEngine(process))
        rows.append(dict(id=len(rows), spot=spot, strike=strike, days=days, sigma=sigma,
                         r=rate, q=dividend, b=borrow, call_put=kind, price=option.NPV(),
                         delta=option.delta(), gamma=option.gamma(), vega=option.vega()/100,
                         theta=option.theta()/365, rho=option.rho()/100))
report = dict(oracle='QuantLib', version=ql.__version__, engine='AnalyticEuropeanEngine',
              source='https://github.com/lballabio/QuantLib/tree/v1.40', valuation_date='2026-04-09',
              convention='Actual365Fixed; continuous q+b carry; vega/rho per 1%; daily theta', scenarios=rows)
content = (json.dumps(report, indent=2, allow_nan=False) + '\n').encode()
target = Path(__file__).resolve().parents[1] / 'vectors/quantlib-rc.json'
parser = argparse.ArgumentParser(); parser.add_argument('--check', action='store_true'); args=parser.parse_args()
if args.check:
    assert target.read_bytes() == content, 'Independent QuantLib regeneration differs from frozen corpus'
else:
    target.write_bytes(content)
print(json.dumps(dict(scenarios=len(rows), sha256=hashlib.sha256(content).hexdigest(), quantlib=ql.__version__)))
