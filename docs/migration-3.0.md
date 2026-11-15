# Migrating to the MarketData 3 graph

OptionPricing 3.0.0 targets both `net10.0` and `net11.0`. Rebuild consumers against the complete Curves, MarketData and adapter 3.0.0 families; do not mix Curves 2.x packages with MarketData 3.x.

All first-party dependencies are signed packages from nuget.org. Curves 3.0.0 supplies both framework assets and uses MarketData 3.0.0. Numerics remains at published 2.0.0, Storage at 2.0.1 and Domain at 1.0.1. Exact archive identities and source revisions are recorded in `dependency-source.json`.

The option-pricing algorithms and public pricing signatures are unchanged by this dependency migration. The existing .NET 10 numerical contract and independently validated .NET 11 decimal precision remain in place.

Applications that directly request price histories must declare the required price basis and handle the typed unsupported-basis and mismatch failures described in the [MarketData 3 migration guide](https://github.com/boutquin/Boutquin.MarketData/blob/v3.0.0/docs/migration-3.0.md). Existing persisted price data must follow that guide's basis-aware migration procedure.

Upgrade all seven OptionPricing packages together to 3.0.0 and rebuild consumers. The 2.0.0 changelog remains historical release documentation.
