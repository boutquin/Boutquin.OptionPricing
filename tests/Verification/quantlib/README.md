# Pinned independent QuantLib qualification

Install `requirements.txt` in an isolated environment. QuantLib 1.40 is an independent C++ OSS implementation reached through its Python bindings; the generators do not import OptionPricing algorithms. The installation receipt records the downloaded wheel URL and hash.

- `generate.py`: 576 calls/puts from AnalyticEuropeanEngine, spanning moneyness, 7–1095 day expiry, volatility, negative/positive rates, and q+b carry. Prices and five scaled Greeks use absolute tolerance 1e-6. Curve parity is checked separately.
- `generate_trees.py`: 96 European/American cases against FdBlackScholesVanillaEngine (400 time steps, 800 space grid), independently checked against a 200/400 grid within 0.003. The 512-step CRR comparison allows 0.05 price units for different numerical discretizations; it is not a claim of exact algorithm identity.
- `generate_heston.py`: eight genuine stochastic-volatility call/put values from AnalyticHestonEngine, covering regular and non-Feller parameters with nonzero borrow. Integration orders 144/192 agree within 1e-8. Monte Carlo consumers must additionally account for sampling error and Euler discretization bias.

Each generator accepts `--check` to rerun the OSS engine and compare the checked-in bytes. Existing Python replicas remain separately identified; they are not treated as independent external implementations. Golden vectors are loaded by the C# verification suite in source and package-only qualification.

Sources: https://github.com/lballabio/QuantLib/releases/tag/v1.40 and https://github.com/lballabio/QuantLib/tree/v1.40/ql/pricingengines .
