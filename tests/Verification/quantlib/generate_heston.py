"""Pinned QuantLib characteristic-function oracle for stochastic-volatility MC."""
import argparse,hashlib,itertools,json
from pathlib import Path
import QuantLib as ql
assert ql.__version__=='1.40'
v=ql.Date(9,4,2026);ql.Settings.instance().evaluationDate=v;dc=ql.Actual365Fixed();rows=[]
for family,params in [('regular',dict(kappa=1.5,theta=.04,sigma_v=.25,rho=-.6,v0=.04)),('non-feller',dict(kappa=.5,theta=.04,sigma_v=.7,rho=-.6,v0=.04))]:
 process=ql.HestonProcess(ql.YieldTermStructureHandle(ql.FlatForward(v,.03,dc)),ql.YieldTermStructureHandle(ql.FlatForward(v,.03,dc)),ql.QuoteHandle(ql.SimpleQuote(100.)),params['v0'],params['kappa'],params['theta'],params['sigma_v'],params['rho'])
 model=ql.HestonModel(process)
 for strike,kind in itertools.product((90.,110.),('Call','Put')):
  option=ql.VanillaOption(ql.PlainVanillaPayoff(ql.Option.Call if kind=='Call' else ql.Option.Put,strike),ql.EuropeanExercise(v+365))
  option.setPricingEngine(ql.AnalyticHestonEngine(model,144));price=option.NPV()
  option.setPricingEngine(ql.AnalyticHestonEngine(model,192));refined=option.NPV()
  assert abs(price-refined)<1e-8
  rows.append(dict(id=len(rows),family=family,spot=100.,strike=strike,days=365,r=.03,q=.01,b=.02,call_put=kind,price=refined,**params))
data=dict(oracle='QuantLib',version=ql.__version__,engine='AnalyticHestonEngine',integrationOrders=[144,192],absoluteRefinementTolerance=1e-8,valuation_date='2026-04-09',scenarios=rows)
content=(json.dumps(data,indent=2)+'\n').encode();p=Path(__file__).resolve().parents[1]/'vectors/quantlib-heston-rc.json'
a=argparse.ArgumentParser();a.add_argument('--check',action='store_true');args=a.parse_args()
if args.check:assert p.read_bytes()==content
else:p.write_bytes(content)
print(json.dumps(dict(scenarios=len(rows),sha256=hashlib.sha256(content).hexdigest())))
