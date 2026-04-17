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
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Adapter.NewYorkFed;
using Boutquin.MarketData.Adapter.UsTreasury;
using Boutquin.MarketData.Abstractions.Calendars;
using Boutquin.MarketData.Abstractions.Diagnostics;
using Boutquin.MarketData.DependencyInjection;
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Examples.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
        await entry.Run().ConfigureAwait(false);
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
        ["01"] = Async("01", "Bootstrapped curve pricing",           Example06BootstrappedCurvePricing),
        ["02"] = Async("02", "Term-structure Greeks",                Example11TermStructureGreeks),
        ["03"] = Sync ("03", "FromCurveGroup factory pricing",       Example12FromCurveGroupFactory),
        ["04"] = Sync ("04", "Multi-curve with borrow cost",         Example13MultiCurveWithBorrowCost),
        ["05"] = Async("05", "Diagnostic-gated pricing",             Example14DiagnosticGatedPricing),
        ["06"] = Async("06", "Real market data pricing (live APIs)", Example15RealMarketDataPricing),
    };
}

static string? ParseExampleArg(string[] args, out string? error)
{
    error = null;
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--example" && i + 1 < args.Length)
            return NormalizeId(args[i + 1]);
        if (args[i].StartsWith("--example=", StringComparison.Ordinal))
            return NormalizeId(args[i]["--example=".Length..]);
    }
    return null;
}

static string NormalizeId(string raw)
{
    // Accept "6" or "06" → always return two-digit form
    if (int.TryParse(raw, out int n))
        return n.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
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

static async Task Example06BootstrappedCurvePricing()
{
    DateOnly valuationDate = FixtureData.FixtureDate;
    var option = new EuropeanOption(CallPut.Call, 100m, valuationDate.AddMonths(6));

    var flatMarket = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.03d, 0.01d);
    var flatResult = new BlackScholesPricer().Price(option, flatMarket);

    var bootstrappedMarket = await MarketHelpers.CreateBootstrappedMarketAsync(valuationDate, 100m, 0.20d).ConfigureAwait(false);
    var bootstrappedResult = new BlackScholesPricer().Price(option, bootstrappedMarket);

    var flatDf = flatMarket.DiscountFactor(option.ExpiryDate);
    var bootstrappedDf = bootstrappedMarket.DiscountFactor(option.ExpiryDate);

    Console.WriteLine($"Example 01 | Flat-curve BS price:         {flatResult.Price:F4}  (DF at expiry: {flatDf:F6})");
    Console.WriteLine($"Example 01 | Bootstrapped-curve BS price: {bootstrappedResult.Price:F4}  (DF at expiry: {bootstrappedDf:F6})");
}

static async Task Example11TermStructureGreeks()
{
    DateOnly valuationDate = FixtureData.FixtureDate;
    var option = new EuropeanOption(CallPut.Call, 100m, valuationDate.AddMonths(6));
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
        snapshot.CurveGroup, CurrencyCode.USD, 100m, 0.20d, new BenchmarkName("SOFR"));
    var tsGreeks = greekProvider.CalculateGreeks(option, tsMarket);

    Console.WriteLine($"Example 02 | Flat-curve rho:        {flatGreeks.Rho:F6}");
    Console.WriteLine($"Example 02 | Term-structure rho:    {tsGreeks.Rho:F6}");
    Console.WriteLine($"Example 02 | Rho difference:        {tsGreeks.Rho - flatGreeks.Rho:F6}");
}

static void Example12FromCurveGroupFactory()
{
    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    CurrencyCode usd = CurrencyCode.USD;

    var discountCurve = new FlatDiscountCurve(new CurveName("USD-DISC"), valuationDate, usd, 0.05);
    var dividendCurve = new FlatDiscountCurve(new CurveName("DIVIDEND"), valuationDate, usd, 0.02);
    var group = new CurveGroupBuilder(new CurveGroupName("USD-EQUITY"), valuationDate)
        .Add(new CurveReference(CurveRole.Discount, usd), discountCurve)
        .Add(new CurveReference(CurveRole.Dividend, usd), dividendCurve)
        .Build();

    var market = OptionMarketData.FromCurveGroup(group, usd, 100m, 0.20d);
    var option = new EuropeanOption(CallPut.Call, 100m, valuationDate.AddMonths(6));
    var result = new BlackScholesPricer().Price(option, market);

    Console.WriteLine($"Example 03 | FromCurveGroup factory price: {result.Price:F4}");
    Console.WriteLine($"Example 03 | Risk-free rate:  {market.RiskFreeRate(option.ExpiryDate):F4}");
    Console.WriteLine($"Example 03 | Dividend yield:  {market.DividendYield(option.ExpiryDate):F4}");
}

static void Example13MultiCurveWithBorrowCost()
{
    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    CurrencyCode usd = CurrencyCode.USD;

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

    var option = new EuropeanOption(CallPut.Call, 100m, valuationDate.AddMonths(6));
    var pricer = new BlackScholesPricer();
    var priceNoBorrow = pricer.Price(option, marketNoBorrow).Price;
    var priceWithBorrow = pricer.Price(option, marketWithBorrow).Price;

    Console.WriteLine($"Example 04 | No borrow:    price={priceNoBorrow:F4}, fwd={marketNoBorrow.ForwardPrice(option.ExpiryDate):F4}");
    Console.WriteLine($"Example 04 | With borrow:  price={priceWithBorrow:F4}, fwd={marketWithBorrow.ForwardPrice(option.ExpiryDate):F4}");
    Console.WriteLine($"Example 04 | Borrow impact: {priceWithBorrow - priceNoBorrow:F4}");
}

static async Task Example14DiagnosticGatedPricing()
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

    var market = OptionMarketData.FromCurveGroup(snapshot.CurveGroup, CurrencyCode.USD, 100m, 0.20d, new BenchmarkName("SOFR"));
    var option = new EuropeanOption(CallPut.Call, 100m, valuationDate.AddMonths(6));
    var result = new BlackScholesPricer().Price(option, market);
    Console.WriteLine($"Example 05 | Diagnostic-gated BS price: {result.Price:F4}");
}

static async Task Example15RealMarketDataPricing()
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

        int apiCount      = snapshot.Provenance.Count(p => p.RetrievalMode == RetrievalMode.Api);
        int snapshotCount = snapshot.Provenance.Count(p => p.RetrievalMode == RetrievalMode.Snapshot);
        int cacheCount    = snapshot.Provenance.Count(p => p.RetrievalMode == RetrievalMode.Cache);
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

        int nodeCount     = recipe.Curves.Sum(c => c.Nodes.Count);
        int resolvedCount = nodeCount - snapshot.DataIssues.Count(i => i.Code.Value is "NODE_MISSING" or "FETCH_FAILED");
        int failedFetches = snapshot.DataIssues.Count(i => i.Code.Value == "FETCH_FAILED");
        int missingNodes  = snapshot.DataIssues.Count(i => i.Code.Value == "NODE_MISSING");

        Console.WriteLine($"Example 06 | Nodes requested:       {nodeCount}");
        Console.WriteLine($"Example 06 | Nodes resolved:        {resolvedCount} of {nodeCount}");
        if (failedFetches > 0)
            Console.WriteLine($"Example 06 | Failed data fetches:   {failedFetches}");
        if (missingNodes > 0)
            Console.WriteLine($"Example 06 | Missing node data:     {missingNodes}");

        foreach (var issue in snapshot.DataIssues.DistinctBy(i => i.Message))
            Console.WriteLine($"             [{issue.Severity}] {issue.Code}: {issue.Message}");

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
            snapshot.CurveGroup, CurrencyCode.USD, 100m, 0.20d, new BenchmarkName("SOFR"));
        var option = new EuropeanOption(CallPut.Call, 100m, today.AddMonths(6));
        var result = new BlackScholesPricer().Price(option, market);

        var discountRef = new CurveReference(CurveRole.Discount, CurrencyCode.USD, new BenchmarkName("SOFR"));
        var curve = (IDiscountCurve)snapshot.CurveGroup.GetCurve(discountRef);
        double df6m = curve.DiscountFactor(today.AddMonths(6));

        Console.WriteLine($"Example 06 | Live-curve BS price:  {result.Price:F4}  (6M DF: {df6m:F6})");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Example 06 | SKIPPED: {ex.GetType().Name}: {ex.Message}");
        Console.WriteLine("             (This example requires network access to NY Fed and US Treasury APIs)");
    }
}

// ── Types ─────────────────────────────────────────────────────────────────────

sealed record ExampleEntry(string Id, string Description, Func<Task> Run);
