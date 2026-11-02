// Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
//
//   Licensed under the Apache License, Version 2.0 (the "License").
//   You may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//
//   See the License for the specific language governing permissions and
//   limitations under the License.
//

using Boutquin.Curves.Abstractions.Curves;
using Boutquin.Curves.Abstractions.Identifiers;
using Boutquin.Curves.Core;
using Boutquin.Curves.Core.Discounting;
using Boutquin.Curves.Recipes;
using Boutquin.Curves.Recipes.Testing;
using Boutquin.Domain.Enumerations;
using Boutquin.MarketData.Abstractions.Calendars;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Adapter.NewYorkFed;
using Boutquin.MarketData.Adapter.UsTreasury;
using Boutquin.MarketData.DependencyInjection;
using Boutquin.Numerics.Random;
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Analytics.StochasticVolatility;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Examples.Shared;
using Boutquin.OptionPricing.Trees.Binomial;
using Boutquin.OptionPricing.Trees.Pricing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

Console.WriteLine("Boutquin.OptionPricing Infrastructure Examples");
Console.WriteLine(new string('─', 50));

var registry = BuildExampleRegistry();

if (args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp(registry);
    return;
}

string? selectedId = ParseExampleArg(args, out string? parseError);
if (parseError is not null)
{
    Console.Error.WriteLine($"Error: {parseError}");
    Console.Error.WriteLine("Use --help to see available examples.");
    return;
}

PrintListing(registry, selectedId);
Console.WriteLine();

if (selectedId is not null)
{
    if (!registry.TryGetValue(selectedId, out var entry))
    {
        Console.Error.WriteLine($"Example '{selectedId}' not found. Use --help to see available examples.");
        return;
    }
    await entry.Run().ConfigureAwait(false);
}
else
{
    foreach (var entry in registry.Values)
    {
        await entry.Run().ConfigureAwait(false);
    }
}

// ── Registry ──────────────────────────────────────────────────────────────────

static SortedDictionary<string, ExampleEntry> BuildExampleRegistry()
{
    static ExampleEntry Sync(string id, string desc, Action run) =>
        new(id, desc, () => { run(); return Task.CompletedTask; });
    static ExampleEntry Async(string id, string desc, Func<Task> run) =>
        new(id, desc, run);

    return new SortedDictionary<string, ExampleEntry>(StringComparer.OrdinalIgnoreCase)
    {
        ["01"] = Async("01", "Bootstrapped curve pricing", Example01BootstrappedCurvePricing),
        ["02"] = Async("02", "Term-structure Greeks", Example02TermStructureGreeks),
        ["03"] = Sync("03", "FromCurveGroup factory pricing", Example03FromCurveGroupFactory),
        ["04"] = Sync("04", "Multi-curve with borrow cost", Example04MultiCurveWithBorrowCost),
        ["05"] = Async("05", "Diagnostic-gated pricing", Example05DiagnosticGatedPricing),
        ["06"] = Async("06", "Real market data pricing (live APIs)", Example06RealMarketDataPricing),
        ["21"] = Sync("21", "Discrete dividends (Merton escrowed)", Example21DiscreteDividends),
        ["22"] = Sync("22", "Tree Greeks (lattice finite differences)", Example22TreeGreeks),
        ["23"] = Sync("23", "Tree dividends (CRR, with vs. without continuous yield)", Example23TreeDividends),
        ["24"] = Sync("24", "Heston stochastic-vol MC (smile)", Example24HestonSmile),
    };
}

static string? ParseExampleArg(string[] args, out string? error)
{
    error = null;
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--example" && i + 1 < args.Length)
        {
            return NormalizeId(args[i + 1]);
        }
        if (args[i].StartsWith("--example=", StringComparison.Ordinal))
        {
            return NormalizeId(args[i]["--example=".Length..]);
        }
    }
    return null;
}

static string NormalizeId(string raw)
{
    // Accept "6" or "06" → always return two-digit form
    if (int.TryParse(raw, out int n))
    {
        return n.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    }
    return raw;
}

static void PrintListing(SortedDictionary<string, ExampleEntry> registry, string? selectedId)
{
    Console.WriteLine("Usage: dotnet run -- [--example <id>] [--help]");
    Console.WriteLine();
    Console.WriteLine("Infrastructure examples (market data, curves, DI):");
    foreach (var (id, entry) in registry)
    {
        string marker = string.Equals(id, selectedId, StringComparison.OrdinalIgnoreCase) ? "▶" : " ";
        Console.WriteLine($"  {marker} --example {entry.Id}  {entry.Description}");
    }
}

static void PrintHelp(SortedDictionary<string, ExampleEntry> registry) =>
    PrintListing(registry, selectedId: null);

static async Task Example01BootstrappedCurvePricing()
{
    DateOnly valuationDate = FixtureData.FixtureDate;
    var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));

    var flatMarket = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);
    var flatResult = new BlackScholesPricer().Price(option, flatMarket);

    var bootstrappedMarket = await MarketHelpers.CreateBootstrappedMarketAsync(valuationDate, 100m, 0.20d).ConfigureAwait(false);
    var bootstrappedResult = new BlackScholesPricer().Price(option, bootstrappedMarket);

    var flatDf = flatMarket.DiscountFactor(option.ExpiryDate);
    var bootstrappedDf = bootstrappedMarket.DiscountFactor(option.ExpiryDate);

    Console.WriteLine($"Example 01 | Flat-curve BS price:         {flatResult.Price.Amount:F4}  (DF at expiry: {flatDf:F6})");
    Console.WriteLine($"Example 01 | Bootstrapped-curve BS price: {bootstrappedResult.Price.Amount:F4}  (DF at expiry: {bootstrappedDf:F6})");
}

static async Task Example02TermStructureGreeks()
{
    DateOnly valuationDate = FixtureData.FixtureDate;
    var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
    var greekProvider = new BlackScholesGreeks();

    var flatMarket = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);
    var flatGreeks = greekProvider.CalculateGreeks(option, flatMarket);

    var snapshot = await MarketHelpers.BuildUsdSnapshotAsync(valuationDate).ConfigureAwait(false);

    if (snapshot.Diagnostics.Repricing.Count > 0)
    {
        double maxError = snapshot.Diagnostics.Repricing.Max(r => r.AbsoluteError);
        if (maxError > 1e-6)
        {
            Console.WriteLine($"Example 02 | BLOCKED: max repricing error {maxError:E2}");
            return;
        }
    }

    var tsMarket = OptionMarketData.FromCurveGroup(
        snapshot.CurveGroup, Currency.USD, 100m, 0.20d, new BenchmarkName("SOFR"));
    var tsGreeks = greekProvider.CalculateGreeks(option, tsMarket);

    Console.WriteLine($"Example 02 | Flat-curve rho:        {flatGreeks.Rho:F6}");
    Console.WriteLine($"Example 02 | Term-structure rho:    {tsGreeks.Rho:F6}");
    Console.WriteLine($"Example 02 | Rho difference:        {tsGreeks.Rho - flatGreeks.Rho:F6}");
}

static void Example03FromCurveGroupFactory()
{
    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    Currency usd = Currency.USD;

    var discountCurve = new FlatDiscountCurve(new CurveName("USD-DISC"), valuationDate, usd, 0.05);
    var dividendCurve = new FlatDiscountCurve(new CurveName("DIVIDEND"), valuationDate, usd, 0.02);
    var group = new CurveGroupBuilder(new CurveGroupName("USD-EQUITY"), valuationDate)
        .Add(new CurveReference(CurveRole.Discount, usd), discountCurve)
        .Add(new CurveReference(CurveRole.Dividend, usd), dividendCurve)
        .Build();

    var market = OptionMarketData.FromCurveGroup(group, usd, 100m, 0.20d);
    var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
    var result = new BlackScholesPricer().Price(option, market);

    Console.WriteLine($"Example 03 | FromCurveGroup factory price: {result.Price.Amount:F4}");
    Console.WriteLine($"Example 03 | Risk-free rate:  {market.RiskFreeRate(option.ExpiryDate):F4}");
    Console.WriteLine($"Example 03 | Dividend yield:  {market.DividendYield(option.ExpiryDate):F4}");
}

static void Example04MultiCurveWithBorrowCost()
{
    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    Currency usd = Currency.USD;

    var discountCurve = new FlatDiscountCurve(new CurveName("USD-DISC"), valuationDate, usd, 0.05);
    var dividendCurve = new FlatDiscountCurve(new CurveName("DIVIDEND"), valuationDate, usd, 0.02);
    var borrowCurve = new FlatDiscountCurve(new CurveName("BORROW"), valuationDate, usd, 0.015);
    var group = new CurveGroupBuilder(new CurveGroupName("USD-EQUITY-BORROW"), valuationDate)
        .Add(new CurveReference(CurveRole.Discount, usd), discountCurve)
        .Add(new CurveReference(CurveRole.Dividend, usd), dividendCurve)
        .Add(new CurveReference(CurveRole.Borrow, usd), borrowCurve)
        .Build();

    var marketWithBorrow = OptionMarketData.FromCurveGroup(group, usd, 100m, 0.20d);

    var noBorrowGroup = new CurveGroupBuilder(new CurveGroupName("USD-EQUITY"), valuationDate)
        .Add(new CurveReference(CurveRole.Discount, usd), discountCurve)
        .Add(new CurveReference(CurveRole.Dividend, usd), dividendCurve)
        .Build();
    var marketNoBorrow = OptionMarketData.FromCurveGroup(noBorrowGroup, usd, 100m, 0.20d);

    var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
    var pricer = new BlackScholesPricer();
    var priceNoBorrow = pricer.Price(option, marketNoBorrow).Price;
    var priceWithBorrow = pricer.Price(option, marketWithBorrow).Price;

    Console.WriteLine($"Example 04 | No borrow:    price={priceNoBorrow.Amount:F4}, fwd={marketNoBorrow.ForwardPrice(option.ExpiryDate):F4}");
    Console.WriteLine($"Example 04 | With borrow:  price={priceWithBorrow.Amount:F4}, fwd={marketWithBorrow.ForwardPrice(option.ExpiryDate):F4}");
    Console.WriteLine($"Example 04 | Borrow impact: {(priceWithBorrow - priceNoBorrow).Amount:F4}");
}

static async Task Example05DiagnosticGatedPricing()
{
    DateOnly valuationDate = FixtureData.FixtureDate;
    var snapshot = await MarketHelpers.BuildUsdSnapshotAsync(valuationDate).ConfigureAwait(false);

    double maxError = snapshot.Diagnostics.Repricing.Count > 0
        ? snapshot.Diagnostics.Repricing.Max(r => r.AbsoluteError)
        : 0d;

    const double threshold = 1e-6;
    Console.WriteLine($"Example 05 | Repricing instruments: {snapshot.Diagnostics.Repricing.Count}");
    Console.WriteLine($"Example 05 | Max repricing error:   {maxError:E2}");

    if (maxError > threshold)
    {
        Console.WriteLine($"Example 05 | BLOCKED: max error {maxError:E2} exceeds threshold {threshold:E2}");
        return;
    }

    var market = OptionMarketData.FromCurveGroup(snapshot.CurveGroup, Currency.USD, 100m, 0.20d, new BenchmarkName("SOFR"));
    var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, valuationDate.AddMonths(6));
    var result = new BlackScholesPricer().Price(option, market);
    Console.WriteLine($"Example 05 | Diagnostic-gated BS price: {result.Price.Amount:F4}");
}

static async Task Example06RealMarketDataPricing()
{
    Console.WriteLine();
    Console.WriteLine("Example 06 | Real market data pricing (live SOFR + Treasury)");

    var services = new ServiceCollection();
    services.AddAnalytics();
    services.AddMarketDataKernel();
    services.AddMarketDataNewYorkFed();
    services.AddMarketDataUsTreasury();
    services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

    var sp = services.BuildServiceProvider();
    var curveBuilder = sp.GetRequiredService<CurveBuilder>();
    var calendar = sp.GetRequiredService<IBusinessCalendar>();

    DateOnly today = calendar.Advance(DateOnly.FromDateTime(DateTime.Today), -1);

    try
    {
        var recipe = StandardCurveRecipes.UsdSofrDiscount(calendar: calendar);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var snapshot = await curveBuilder.BuildAsync(recipe, today).ConfigureAwait(false);
        sw.Stop();

        int apiCount = snapshot.Provenance.Count(p => p.RetrievalMode == RetrievalMode.Api);
        int snapshotCount = snapshot.Provenance.Count(p => p.RetrievalMode == RetrievalMode.Snapshot);
        int cacheCount = snapshot.Provenance.Count(p => p.RetrievalMode == RetrievalMode.Cache);
        string source = snapshotCount + cacheCount > 0 && apiCount == 0
            ? $"cached ({snapshotCount} from L2 disk, {cacheCount} from L1 memory, {sw.ElapsedMilliseconds} ms)"
            : apiCount > 0 && snapshotCount + cacheCount == 0
                ? $"live API fetch ({sw.ElapsedMilliseconds} ms)"
                : $"mixed ({apiCount} live, {snapshotCount + cacheCount} cached, {sw.ElapsedMilliseconds} ms)";

        Console.WriteLine($"Example 06 | Valuation date:        {today}");
        Console.WriteLine($"Example 06 | Data source:           {source}");
        foreach (var prov in snapshot.Provenance.DistinctBy(p => p.Dataset))
        {
            var dateTag = prov.DataDate.HasValue ? $"{prov.Freshness} {prov.DataDate.Value:yyyy-MM-dd}" : $"{prov.Freshness}";
            Console.WriteLine($"             {prov.Dataset}: {prov.RetrievalMode} via {prov.Provider} [{dateTag}]");
        }

        int nodeCount = recipe.Curves.Sum(c => c.Nodes.Count);
        int resolvedCount = nodeCount - snapshot.DataIssues.Count(i => i.Code.Value is "NODE_MISSING" or "FETCH_FAILED");
        int failedFetches = snapshot.DataIssues.Count(i => i.Code.Value == "FETCH_FAILED");
        int missingNodes = snapshot.DataIssues.Count(i => i.Code.Value == "NODE_MISSING");

        Console.WriteLine($"Example 06 | Nodes requested:       {nodeCount}");
        Console.WriteLine($"Example 06 | Nodes resolved:        {resolvedCount} of {nodeCount}");
        if (failedFetches > 0)
        {
            Console.WriteLine($"Example 06 | Failed data fetches:   {failedFetches}");
        }
        if (missingNodes > 0)
        {
            Console.WriteLine($"Example 06 | Missing node data:     {missingNodes}");
        }

        foreach (var issue in snapshot.DataIssues.DistinctBy(i => i.Message))
        {
            Console.WriteLine($"             [{issue.Severity}] {issue.Code}: {issue.Message}");
        }

        if (resolvedCount == 0)
        {
            Console.WriteLine($"Example 06 | SKIPPED: no data resolved — check network connectivity");
            return;
        }

        Console.WriteLine($"Example 06 | Data retrieval:        OK ({resolvedCount} nodes from live sources)");

        if (snapshot.Diagnostics.Repricing.Count > 0)
        {
            double maxError = snapshot.Diagnostics.Repricing.Max(r => r.AbsoluteError);
            Console.WriteLine($"Example 06 | Repricing instruments: {snapshot.Diagnostics.Repricing.Count}");
            Console.WriteLine($"Example 06 | Max repricing error:   {maxError:E2}");

            if (maxError > 1e-4)
            {
                Console.WriteLine($"Example 06 | SKIPPED: repricing error too large for pricing");
                return;
            }
        }

        var market = OptionMarketData.FromCurveGroup(
            snapshot.CurveGroup, Currency.USD, 100m, 0.20d, new BenchmarkName("SOFR"));
        var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, today.AddMonths(6));
        var result = new BlackScholesPricer().Price(option, market);

        var discountRef = new CurveReference(CurveRole.Discount, Currency.USD, new BenchmarkName("SOFR"));
        var curve = (IDiscountCurve)snapshot.CurveGroup.GetCurve(discountRef);
        double df6m = curve.DiscountFactor(today.AddMonths(6));

        Console.WriteLine($"Example 06 | Live-curve BS price:  {result.Price.Amount:F4}  (6M DF: {df6m:F6})");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Example 06 | SKIPPED: {ex.GetType().Name}: {ex.Message}");
        Console.WriteLine("             (This example requires network access to NY Fed and US Treasury APIs)");
    }
}

static void Example21DiscreteDividends()
{
    Console.WriteLine();
    Console.WriteLine("Example 21 | Discrete dividends (Merton escrowed-dividend approximation)");

    DateOnly valuationDate = FixtureData.FixtureDate;
    DateOnly expiry = valuationDate.AddDays(365);

    // Flat 5% discount curve, 20% vol, spot 100, ATM one-year call.
    var market = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, 0.0d);
    var option = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);

    // Two $0.80 cash dividends, ex-dates at 90 and 270 days (both before expiry).
    var schedule = new DividendSchedule(new[]
    {
        new DividendPayment(valuationDate.AddDays(90), 0.80m),
        new DividendPayment(valuationDate.AddDays(270), 0.80m),
    });

    // No-dividend baseline: plain Black-Scholes on the same market.
    var noDivResult = new BlackScholesPricer().Price(option, market);

    // Merton escrowed-dividend price on the same market + schedule.
    var mertonResult = new MertonDiscreteDividendPricer(schedule).Price(option, market);
    var diag = mertonResult.GetDiagnostics();

    Console.WriteLine($"Example 21 | No-dividend BS call price:   {noDivResult.Price.Amount:F4}");
    Console.WriteLine($"Example 21 | Merton escrowed call price:  {mertonResult.Price.Amount:F4}");
    Console.WriteLine($"Example 21 | PV(dividends):               {(double)diag["pvDividends"]!:F6}");
    Console.WriteLine($"Example 21 | Escrowed spot S*:            {(double)diag["escrowedSpot"]!:F6}");
    Console.WriteLine($"Example 21 | Uplifted volatility sigma*:  {(double)diag["upliftedVolatility"]!:F6}");
    Console.WriteLine($"Example 21 | Dividend impact:             {(mertonResult.Price - noDivResult.Price).Amount:F4}");
    Console.WriteLine($"Example 21 | Model: {mertonResult.ModelName}");
}

static void Example22TreeGreeks()
{
    Console.WriteLine();
    Console.WriteLine("Example 22 | Tree Greeks (lattice finite differences, Chriss Figure 7.8.1)");

    DateOnly valuationDate = FixtureData.FixtureDate;
    DateOnly expiry = valuationDate.AddMonths(6);

    // Spot 100, 20% vol, 5% rate, no dividend; ATM six-month options.
    var market = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, 0.0d);
    var europeanCall = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);
    var americanPut = new AmericanOption(CallPut.Put, 100m, Currency.USD, expiry);

    // Build a 400-step CRR tree, value it retaining the full lattice, then read the
    // hedge parameters off the lattice with no bump-and-reprice.
    var input = TreeModelInput.FromMarketData(market, europeanCall.Strike.Amount, expiry, 400);
    var tree = new CoxRossRubinsteinTreeBuilder().Build(input);

    var calculator = new TreeGreeksCalculator();

    var europeanLattice = new EuropeanTreePricer().PriceWithLattice(europeanCall, market, tree);
    var europeanGreeks = calculator.Compute(europeanLattice);

    // Analytical Black-Scholes Greeks for the same European call, for side-by-side comparison.
    var analytic = new BlackScholesGreeks().CalculateGreeks(europeanCall, market);

    var americanLattice = new AmericanTreePricer().PriceWithLattice(americanPut, market, tree);
    var americanGreeks = calculator.Compute(americanLattice);

    Console.WriteLine($"Example 22 | European call lattice price:  {europeanLattice.RootValue:F4}");
    Console.WriteLine($"Example 22 | European Delta (tree / BS):   {europeanGreeks.Delta:F6} / {analytic.Delta:F6}");
    Console.WriteLine($"Example 22 | European Gamma (tree / BS):   {europeanGreeks.Gamma:F6} / {analytic.Gamma:F6}");
    Console.WriteLine($"Example 22 | European Theta (tree / BS):   {europeanGreeks.Theta:F6} / {analytic.Theta:F6}  (per calendar day)");
    Console.WriteLine($"Example 22 | American put lattice price:   {americanLattice.RootValue:F4}");
    Console.WriteLine($"Example 22 | American put Delta:           {americanGreeks.Delta:F6}  (in [-1, 0])");
    Console.WriteLine($"Example 22 | American put Gamma:           {americanGreeks.Gamma:F6}  (> 0)");
    Console.WriteLine("Example 22 | Vega / Rho:                   null (require repricing — out of lattice scope)");
}

static void Example23TreeDividends()
{
    Console.WriteLine();
    Console.WriteLine("Example 23 | Tree dividends (CRR, with vs. without continuous yield, Chriss §6.10)");
    Console.WriteLine("             Note: the CRR tree models dividends as a continuous yield (q), not discrete cash payments.");

    DateOnly valuationDate = FixtureData.FixtureDate;
    DateOnly expiry = valuationDate.AddMonths(6);

    // Parameters: spot 100, ATM, 20% vol, 5% rate, 6-month expiry, 200-step CRR tree.
    // Two scenarios: no dividend (q = 0) vs. 3% continuous dividend yield.
    const double r = 0.05d;
    const double sigma = 0.20d;
    const int steps = 200;

    // ── European call: with and without continuous dividend yield ────────────

    var europeanCall = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);

    // No dividend
    var marketNoDiv = MarketHelpers.CreateFlatMarket(valuationDate, 100m, sigma, r, dividendYield: 0.0d);
    var inputNoDiv = TreeModelInput.FromMarketData(marketNoDiv, europeanCall.Strike.Amount, expiry, steps);
    var treeNoDiv = new CoxRossRubinsteinTreeBuilder().Build(inputNoDiv);
    var euNoDivPrice = new EuropeanTreePricer().Price(europeanCall, marketNoDiv, treeNoDiv);

    // With 3% continuous dividend yield
    var marketWithDiv = MarketHelpers.CreateFlatMarket(valuationDate, 100m, sigma, r, dividendYield: 0.03d);
    var inputWithDiv = TreeModelInput.FromMarketData(marketWithDiv, europeanCall.Strike.Amount, expiry, steps);
    var treeWithDiv = new CoxRossRubinsteinTreeBuilder().Build(inputWithDiv);
    var euWithDivPrice = new EuropeanTreePricer().Price(europeanCall, marketWithDiv, treeWithDiv);

    Console.WriteLine($"Example 23 | European call (q = 0%):    price = {euNoDivPrice.Price.Amount:F4}");
    Console.WriteLine($"Example 23 | European call (q = 3%):    price = {euWithDivPrice.Price.Amount:F4}");
    Console.WriteLine($"Example 23 | European impact of q=3%:   {(euWithDivPrice.Price - euNoDivPrice.Price).Amount:F4}  (negative: dividend lowers the forward)");

    // ── American call: with and without continuous dividend yield ────────────

    // For the American, we use a deep ITM strike to make the early-exercise boundary
    // visible. Strike 85 (spot 100, ~18% ITM) on the same expiry and vol.
    var americanCall = new AmericanOption(CallPut.Call, 85m, Currency.USD, expiry);

    var amNoDivPrice = new AmericanTreePricer().Price(americanCall, marketNoDiv,
        new CoxRossRubinsteinTreeBuilder().Build(
            TreeModelInput.FromMarketData(marketNoDiv, americanCall.Strike.Amount, expiry, steps)));

    var treeWithDivAm = new CoxRossRubinsteinTreeBuilder().Build(
        TreeModelInput.FromMarketData(marketWithDiv, americanCall.Strike.Amount, expiry, steps));
    var amWithDivPrice = new AmericanTreePricer().Price(americanCall, marketWithDiv, treeWithDivAm);

    Console.WriteLine($"Example 23 | American call K=85 (q = 0%):  price = {amNoDivPrice.Price.Amount:F4}");
    Console.WriteLine($"Example 23 | American call K=85 (q = 3%):  price = {amWithDivPrice.Price.Amount:F4}");

    // ── Early-exercise boundary shift (American call, K=70, q=0 vs. q=8%) ──
    // Use a deeper ITM strike and higher yield to make the boundary clearly visible.
    var callForBoundary = new AmericanOption(CallPut.Call, 70m, Currency.USD, expiry.AddMonths(6));
    var pricer = new AmericanTreePricer();

    var treeNoDivBdy = new CoxRossRubinsteinTreeBuilder().Build(
        TreeModelInput.FromMarketData(marketNoDiv, callForBoundary.Strike.Amount, callForBoundary.ExpiryDate, steps));
    var boundaryNoDiv = pricer.ComputeExerciseBoundary(callForBoundary, marketNoDiv, treeNoDivBdy);

    var marketHighDiv = MarketHelpers.CreateFlatMarket(valuationDate, 100m, sigma, r, dividendYield: 0.08d);
    var treeHighDivBdy = new CoxRossRubinsteinTreeBuilder().Build(
        TreeModelInput.FromMarketData(marketHighDiv, callForBoundary.Strike.Amount, callForBoundary.ExpiryDate, steps));
    var boundaryHighDiv = pricer.ComputeExerciseBoundary(callForBoundary, marketHighDiv, treeHighDivBdy);

    Console.WriteLine($"Example 23 | Early-exercise boundary (q = 0%): steps active = {boundaryNoDiv.Boundaries.Count}  (expected: 0 — calls never early-exercised without dividends)");
    Console.WriteLine($"Example 23 | Early-exercise boundary (q = 8%): steps active = {boundaryHighDiv.Boundaries.Count}  (expected: > 0 — dividend yield activates early exercise)");
    Console.WriteLine($"Example 23 | Boundary shift: q=0% → empty, q=8% → {(boundaryHighDiv.IsSuccess ? "non-empty (early exercise activated)" : "still empty — try higher yield or deeper ITM")}");
}

static void Example24HestonSmile()
{
    Console.WriteLine();
    Console.WriteLine("Example 24 | Heston stochastic-vol MC smile (full-truncation Euler, Chriss §8.5)");

    DateOnly valuationDate = FixtureData.FixtureDate;
    DateOnly expiry = valuationDate.AddDays(365);

    // Spot 100, 5% rate, no dividend. The market's flat vol is unused by the Heston pricer
    // (variance is driven by HestonParameters); 20% is supplied only to satisfy the snapshot.
    var market = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, dividendYield: 0.0d);

    // ── sigma_v -> 0 sanity: Heston collapses to Black-Scholes at a flat 20% vol ──
    // v0 = theta = 0.04 = 0.20^2, sigma_v = 0 freezes the variance, so the MC mean must
    // match the closed-form BS price within Monte-Carlo standard error.
    var atmCall = new EuropeanOption(CallPut.Call, 100m, Currency.USD, expiry);
    double bsPrice = (double)new BlackScholesPricer().Price(atmCall, market).Price.Amount;

    var flatParms = new HestonParameters(V0: 0.04d, Kappa: 1.5d, Theta: 0.04d, SigmaV: 0.0d, Rho: 0.0d);
    var flatResult = new SimpleHestonMcPricer(flatParms, paths: 200_000, steps: 64, new Pcg64RandomSource(20260101UL))
        .Price(atmCall, market);
    double flatMc = (double)flatResult.Price.Amount;
    double flatSe = (double)flatResult.GetDiagnostics()["standardError"]!;

    Console.WriteLine($"Example 24 | sigma_v -> 0 check:  BS={bsPrice:F4}  Heston-MC={flatMc:F4}  (±{2d * flatSe:F4}, 95% CI)");
    Console.WriteLine($"Example 24 |                    within MC error: {(Math.Abs(flatMc - bsPrice) < 3d * flatSe ? "YES" : "NO")}");

    // ── Negative-correlation smile: price calls across strikes, invert each to BS IV ──
    // rho = -0.8, sigma_v = 0.6 produce a pronounced negative skew (OTM puts richer than OTM calls).
    var smileParms = new HestonParameters(V0: 0.04d, Kappa: 1.5d, Theta: 0.04d, SigmaV: 0.6d, Rho: -0.8d);
    decimal[] strikes = [80m, 90m, 100m, 110m, 120m];

    Console.WriteLine($"Example 24 | Negative-correlation smile (rho={smileParms.Rho:F2}, sigma_v={smileParms.SigmaV:F2}):");
    bool fellerReported = false;
    foreach (var strike in strikes)
    {
        var call = new EuropeanOption(CallPut.Call, strike, Currency.USD, expiry);
        var result = new SimpleHestonMcPricer(smileParms, paths: 300_000, steps: 64, new Pcg64RandomSource(7UL))
            .Price(call, market);
        double iv = ImplyBlackScholesVol(call, market, (double)result.Price.Amount);
        Console.WriteLine($"             K={strike,4}  price={(double)result.Price.Amount,8:F4}  implied vol={iv:P2}");
        if (!fellerReported)
        {
            Console.WriteLine($"             Feller satisfied: {result.GetDiagnostics()["fellerSatisfied"]}  |  model: {result.ModelName}");
            fellerReported = true;
        }
    }
    Console.WriteLine("Example 24 | Implied vol falls as strike rises — the negative skew of equity-index options.");
}

// Inverts a European option price to its Black-Scholes implied volatility by bisection over the
// monotone closed-form pricer; used only to render the Heston smile in Example 24.
static double ImplyBlackScholesVol(EuropeanOption option, OptionMarketData market, double targetPrice)
{
    var pricer = new BlackScholesPricer();
    double lo = 1e-4d;
    double hi = 5.0d;
    for (int iter = 0; iter < 200; iter++)
    {
        double mid = 0.5d * (lo + hi);
        double price = (double)pricer.Price(option, market.WithFlatVolatility(mid)).Price.Amount;
        if (price > targetPrice)
        {
            hi = mid;
        }
        else
        {
            lo = mid;
        }
    }
    return 0.5d * (lo + hi);
}

// ── Types ─────────────────────────────────────────────────────────────────────

sealed record ExampleEntry(string Id, string Description, Func<Task> Run);
