"""Independent finite-difference reference for the binomial tree pricers."""
import argparse, hashlib, itertools, json
from pathlib import Path
import QuantLib as ql
assert ql.__version__ == '1.40'
valuation=ql.Date(9,4,2026);ql.Settings.instance().evaluationDate=valuation;dc=ql.Actual365Fixed();rows=[]
for strike,days,rate,dividend,borrow,exercise,side in itertools.product((80.,100.,120.),(91,365),(-.01,.04),(0.,.03),(.02,),('European','American'),('Call','Put')):
 process=ql.BlackScholesMertonProcess(ql.QuoteHandle(ql.SimpleQuote(100.)),ql.YieldTermStructureHandle(ql.FlatForward(valuation,dividend+borrow,dc)),ql.YieldTermStructureHandle(ql.FlatForward(valuation,rate,dc)),ql.BlackVolTermStructureHandle(ql.BlackConstantVol(valuation,ql.NullCalendar(),.2,dc)))
 payoff=ql.PlainVanillaPayoff(ql.Option.Call if side=='Call' else ql.Option.Put,strike)
 expiry=valuation+days
 ex=ql.EuropeanExercise(expiry) if exercise=='European' else ql.AmericanExercise(valuation,expiry)
 opt=ql.VanillaOption(payoff,ex)
 opt.setPricingEngine(ql.FdBlackScholesVanillaEngine(process,200,400))
 price=opt.NPV()
 opt.setPricingEngine(ql.FdBlackScholesVanillaEngine(process,400,800))
 refined=opt.NPV()
 assert abs(refined-price)<.003,(price,refined)
 rows.append(dict(id=len(rows),spot=100.,strike=strike,days=days,sigma=.2,r=rate,q=dividend,b=borrow,exercise=exercise,call_put=side,price=refined,coarsePrice=price))
report=dict(oracle='QuantLib',version=ql.__version__,engine='FdBlackScholesVanillaEngine',grid={'time':400,'space':800},convergenceControl={'time':200,'space':400,'absoluteTolerance':.003},treeSteps=512,absolutePriceTolerance=.05,valuation_date='2026-04-09',scenarios=rows)
content=(json.dumps(report,indent=2,allow_nan=False)+'\n').encode();p=Path(__file__).resolve().parents[1]/'vectors/quantlib-trees-rc.json'
a=argparse.ArgumentParser();a.add_argument('--check',action='store_true');args=a.parse_args()
if args.check:assert p.read_bytes()==content
else:p.write_bytes(content)
print(json.dumps(dict(scenarios=len(rows),sha256=hashlib.sha256(content).hexdigest(),maxGridRefinementDifference=max(abs(r['price']-r['coarsePrice']) for r in rows))))
