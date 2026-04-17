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

using Boutquin.Numerics.Distributions;
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Analytics.Hedging;
using Boutquin.OptionPricing.Analytics.Parity;
using Boutquin.OptionPricing.Calibration.ImpliedTrees;
using Boutquin.OptionPricing.Calibration.ImpliedVolatility;
using Boutquin.OptionPricing.Calibration.Smile;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Examples.Shared;
using Boutquin.OptionPricing.Exotics.Barriers;
using Boutquin.OptionPricing.Trees.Binomial;
using Boutquin.OptionPricing.Trees.Pricing;

Console.WriteLine("Black-Scholes and Beyond — Chapter Examples");
Console.WriteLine(new string('─', 50));

var registry = BuildChapterRegistry();

if (args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp(registry);
    return;
}

int? selectedChapter = ParseChapterArg(args, out string? parseError);
if (parseError is not null)
{
    Console.Error.WriteLine($"Error: {parseError}");
    Console.Error.WriteLine("Use --help to see available chapters.");
    return;
}

PrintListing(registry, selectedChapter);
Console.WriteLine();

if (selectedChapter.HasValue)
{
    if (!registry.TryGetValue(selectedChapter.Value, out var chapterInfo))
    {
        Console.Error.WriteLine($"Chapter {selectedChapter.Value} not found. Use --help to see available chapters.");
        return;
    }
    await RunChapterAsync(chapterInfo).ConfigureAwait(false);
}
else
{
    foreach (var chapterInfo in registry.Values)
        await RunChapterAsync(chapterInfo).ConfigureAwait(false);
}

// ── Registry ──────────────────────────────────────────────────────────────────

static SortedDictionary<int, ChapterInfo> BuildChapterRegistry()
{
    static ExampleEntry Sync(string id, string desc, Action run) =>
        new(id, desc, () => { run(); return Task.CompletedTask; });

    return new SortedDictionary<int, ChapterInfo>
    {
        [1] = new(1, "Stocks, Options, and Futures",
        [
            Sync("Ch01", "Security payoff diagrams and arbitrage bounds", Chapter01_SecurityPayoffDiagrams),
        ]),
        [2] = new(2, "Fundamental Mathematical Concepts",
        [
            Sync("Ch02", "Normal distribution properties and N(x) approximations", Chapter02_NormalDistributionProperties),
        ]),
        [3] = new(3, "The Geometric Brownian Motion Model of Price Movements",
        [
            Sync("Ch03", "GBM price distribution and volatility calibration", Chapter03_GbmPriceDistribution),
        ]),
        [4] = new(4, "The Black-Scholes Formula",
        [
            Sync("01", "Black-Scholes vanilla call",            Example01BlackScholesVanilla),
            Sync("02", "Greeks and put-call parity",            Example02GreeksAndParity),
            Sync("18", "Dividend impact on BS price and delta", Example18DividendImpact),
        ]),
        [5] = new(5, "More on the Black-Scholes Formula",
        [
            Sync("16", "Delta-hedging P&L simulation",          Example16DeltaHedgingSimulation),
        ]),
        [6] = new(6, "Binomial Trees",
        [
            Sync("17", "Arrow-Debreu state prices",             Example17ArrowDebreuStatePrices),
        ]),
        [7] = new(7, "Basic Option Pricing with Binomial Trees",
        [
            Sync("03", "Binomial tree convergence to Black-Scholes",  Example03BinomialConvergence),
            Sync("04", "American put early exercise premium",         Example04AmericanPutEarlyExercise),
            Sync("19", "Digital (cash-or-nothing) option on tree",    Example19DigitalOptionOnTree),
        ]),
        [8] = new(8, "The Volatility Smile",
        [
            Sync("05", "Implied volatility recovery",           Example05ImpliedVolatilityRecovery),
            Sync("07", "Smile slice construction",              Example07SmileSliceConstruction),
        ]),
        [9] = new(9, "Implied Volatility Trees",
        [
            Sync("09a", "Derman-Kani implied volatility tree calibration", Example09DermanKaniImpliedTree),
        ]),
        [10] = new(10, "Implied Binomial Trees",
        [
            Sync("10a", "Rubinstein implied binomial tree calibration",    Example10RubinsteinImpliedBinomialTree),
        ]),
        [11] = new(11, "Pricing Barrier Options in the Presence of the Smile",
        [
            Sync("09", "Barrier options under flat volatility",             Example09BarrierOptionFlatVol),
            Sync("10", "Barrier options: smile-aware vs flat-vol pricing",  Example10BarrierOptionSmileAware),
            Sync("20", "Barrier in-out parity verification",                Example20BarrierInOutParity),
        ]),
    };
}

static int? ParseChapterArg(string[] args, out string? error)
{
    error = null;
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--chapter" && i + 1 < args.Length)
        {
            if (int.TryParse(args[i + 1], out int n))
                return n;
            error = $"--chapter requires an integer argument, got '{args[i + 1]}'.";
            return null;
        }
        if (args[i].StartsWith("--chapter=", StringComparison.Ordinal))
        {
            string val = args[i]["--chapter=".Length..];
            if (int.TryParse(val, out int n))
                return n;
            error = $"--chapter requires an integer argument, got '{val}'.";
            return null;
        }
    }
    return null;
}

static void PrintListing(SortedDictionary<int, ChapterInfo> registry, int? selectedChapter)
{
    Console.WriteLine("Usage: dotnet run -- [--chapter <n>] [--help]");
    Console.WriteLine();
    Console.WriteLine("Chapters (Black-Scholes and Beyond, Neil A. Chriss):");
    foreach (var (_, info) in registry)
    {
        string marker = selectedChapter == info.Number ? "▶" : " ";
        Console.WriteLine($"  {marker} --chapter {info.Number,2}  {info.Title}");
        foreach (var ex in info.Examples)
            Console.WriteLine($"                  {ex.Id,-5}  {ex.Description}");
    }
}

static void PrintHelp(SortedDictionary<int, ChapterInfo> registry) =>
    PrintListing(registry, selectedChapter: null);

static async Task RunChapterAsync(ChapterInfo chapter)
{
    Console.WriteLine($"── Chapter {chapter.Number}: {chapter.Title} ──");
    foreach (var ex in chapter.Examples)
        await ex.Run().ConfigureAwait(false);
    Console.WriteLine();
}

static void Example01BlackScholesVanilla()
{
    Console.WriteLine();
    Console.WriteLine("§4.8  The Black-Scholes formula   C = S·N(d₁) − K·e^{−rT}·N(d₂)");
    Console.WriteLine("      S=100, K=100, T=1 yr, r=5%, σ=20%, q=0%");
    Console.WriteLine();

    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    DateOnly expiry        = valuationDate.AddYears(1);
    var callOption = new EuropeanOption(CallPut.Call, 100m, expiry);
    var putOption  = new EuropeanOption(CallPut.Put,  100m, expiry);
    var market     = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, 0.00d);
    var pricer     = new BlackScholesPricer();

    double S     = (double)market.Spot;
    double K     = (double)callOption.Strike;
    double sigma = market.FlatVolatility;
    double r     = market.RiskFreeRate(expiry);
    double T     = market.TimeToExpiryInYears(expiry);
    double sqrtT = Math.Sqrt(T);

    double d1 = (Math.Log(S / K) + (r + sigma * sigma / 2.0) * T) / (sigma * sqrtT);
    double d2 = d1 - sigma * sqrtT;

    var callResult = pricer.Price(callOption, market);
    var putResult  = pricer.Price(putOption,  market);

    Console.WriteLine($"  d₁ = (log(S/K) + (r+σ²/2)·T) / (σ·√T) = {d1:F4}");
    Console.WriteLine($"  d₂ = d₁ − σ·√T                         = {d2:F4}");
    Console.WriteLine($"  N(d₁) = {CumulativeNormal.Evaluate(d1):F6}   N(d₂) = {CumulativeNormal.Evaluate(d2):F6}");
    Console.WriteLine();
    Console.WriteLine($"  Call  C = S·N(d₁) − K·e^{{−rT}}·N(d₂)       = {callResult.Price:F4}");
    Console.WriteLine($"  Put   P = K·e^{{−rT}}·N(−d₂) − S·N(−d₁)     = {putResult.Price:F4}");
    Console.WriteLine($"  Parity: C − P = {callResult.Price - putResult.Price:F4},  S − K·e^{{−rT}} = {S - K * Math.Exp(-r * T):F4}");
}

static void Example02GreeksAndParity()
{
    Console.WriteLine();
    Console.WriteLine("§4.10 The Greeks — first-order price sensitivities");
    Console.WriteLine("      S=100, K=100, T=1 yr, r=5%, σ=20%, q=0%");
    Console.WriteLine();

    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    DateOnly expiry        = valuationDate.AddYears(1);
    var callOption  = new EuropeanOption(CallPut.Call, 100m, expiry);
    var putOption   = new EuropeanOption(CallPut.Put,  100m, expiry);
    var market      = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, 0.00d);
    var pricer      = new BlackScholesPricer();
    var greeks      = new BlackScholesGreeks();

    var callResult  = pricer.Price(callOption, market);
    var putResult   = pricer.Price(putOption,  market);
    var callGreeks  = greeks.CalculateGreeks(callOption, market);
    var putGreeks   = greeks.CalculateGreeks(putOption,  market);
    var impliedCall = PutCallParity.ImpliedCallPrice(putResult.Price, market.Spot, callOption.Strike, market, expiry);

    Console.WriteLine($"  {"Greek",8}  {"Call",12}  {"Put",12}  Parity identity");
    Console.WriteLine($"  {new string('─', 65)}");
    Console.WriteLine($"  {"Price",8}  {callResult.Price,12:F4}  {putResult.Price,12:F4}  C − P = S − K·e^{{−rT}}");
    Console.WriteLine($"  {"Delta",8}  {callGreeks.Delta,12:F6}  {putGreeks.Delta,12:F6}  Δ_C − Δ_P = 1  (q=0)");
    Console.WriteLine($"  {"Gamma",8}  {callGreeks.Gamma,12:F6}  {putGreeks.Gamma,12:F6}  equal for call and put");
    Console.WriteLine($"  {"Vega",8}  {callGreeks.Vega,12:F6}  {putGreeks.Vega,12:F6}  equal for call and put");
    Console.WriteLine($"  {"Theta",8}  {callGreeks.Theta,12:F6}  {putGreeks.Theta,12:F6}  differ by r·K·e^{{−rT}}");
    Console.WriteLine($"  {"Rho",8}  {callGreeks.Rho,12:F6}  {putGreeks.Rho,12:F6}  differ in sign");
    Console.WriteLine();
    Console.WriteLine($"  Parity-implied call = {impliedCall:F4}  (direct BS call = {callResult.Price:F4},  diff = {impliedCall - callResult.Price:E2})");
    Console.WriteLine($"  Δ_C − Δ_P = {callGreeks.Delta - putGreeks.Delta:F6}  (should be 1.000000 when q=0)");
}

static void Example03BinomialConvergence()
{
    Console.WriteLine();
    Console.WriteLine("§7.2  CRR binomial tree convergence to Black-Scholes");
    Console.WriteLine("      S=100, K=100, T=1 yr, r=5%, σ=20%, q=0%");
    Console.WriteLine();

    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    DateOnly expiry        = valuationDate.AddYears(1);
    var option      = new EuropeanOption(CallPut.Call, 100m, expiry);
    var market      = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, 0.00d);
    var analytic    = new BlackScholesPricer().Price(option, market);
    var treePricer  = new EuropeanTreePricer();
    var treeBuilder = new CoxRossRubinsteinTreeBuilder();

    Console.WriteLine($"  Analytic BS price: {analytic.Price:F6}");
    Console.WriteLine();
    Console.WriteLine($"  {"Steps",7}  {"Tree Price",12}  {"Error (bps)",13}");
    Console.WriteLine($"  {new string('─', 37)}");

    int[] stepCounts = [10, 25, 50, 100, 200, 500];
    foreach (int steps in stepCounts)
    {
        var input    = TreeModelInput.FromMarketData(market, option.Strike, expiry, steps);
        var tree     = treeBuilder.Build(input);
        var result   = treePricer.Price(option, market, tree);
        double error = (double)(result.Price - analytic.Price) / (double)analytic.Price * 10000d;
        Console.WriteLine($"  {steps,7}  {result.Price,12:F6}  {error,+13:F2}");
    }
}

static void Example04AmericanPutEarlyExercise()
{
    Console.WriteLine();
    Console.WriteLine("§7.4  American put early exercise premium   K=100, T=1 yr, r=5%, σ=20%, q=0%");
    Console.WriteLine("      American > European when interest on K exceeds option's remaining time value");
    Console.WriteLine();

    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    DateOnly expiry        = valuationDate.AddYears(1);
    var euPricer   = new EuropeanTreePricer();
    var amPricer   = new AmericanTreePricer();
    var treeBuilder = new CoxRossRubinsteinTreeBuilder();

    Console.WriteLine($"  {"Spot",6}  {"European P",12}  {"American P",12}  {"Premium",9}  {"Intrinsic",10}");
    Console.WriteLine($"  {new string('─', 58)}");

    decimal[] spots = [70m, 75m, 80m, 85m, 90m, 95m, 100m, 105m, 110m];
    foreach (decimal s in spots)
    {
        var market    = MarketHelpers.CreateFlatMarket(valuationDate, s, 0.20d, 0.05d, 0.00d);
        var euOption  = new EuropeanOption(CallPut.Put, 100m, expiry);
        var amOption  = new AmericanOption(CallPut.Put, 100m, expiry);
        var input     = TreeModelInput.FromMarketData(market, 100m, expiry, 200);
        var tree      = treeBuilder.Build(input);
        decimal euP   = euPricer.Price(euOption, market, tree).Price;
        decimal amP   = amPricer.Price(amOption, market, tree).Price;
        decimal prem  = amP - euP;
        decimal intr  = Math.Max(100m - s, 0m);
        Console.WriteLine($"  {s,6:F0}  {euP,12:F4}  {amP,12:F4}  {prem,9:F4}  {intr,10:F2}");
    }

    Console.WriteLine();
    Console.WriteLine("  Premium peaks in the money: deep ITM early exercise earns interest on K");
    Console.WriteLine("  immediately rather than waiting; ATM time value dominates, so premium is small.");
}

static void Example05ImpliedVolatilityRecovery()
{
    Console.WriteLine();
    Console.WriteLine("§8.3  Implied volatility — recover σ from observed BS price");
    Console.WriteLine("      S=100, K=100, T=1 yr, r=5%, q=0%;  initial guess σ₀=15% for all solves");
    Console.WriteLine();

    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    DateOnly expiry        = valuationDate.AddYears(1);
    var baseMarket  = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, 0.00d);
    var option      = new EuropeanOption(CallPut.Call, 100m, expiry);
    var pricer      = new BlackScholesPricer();
    var solver      = new ImpliedVolatilitySolver();
    var guess       = baseMarket.WithFlatVolatility(0.15d);

    Console.WriteLine($"  {"True σ",8}  {"BS Price",10}  {"Recovered σ",13}  {"Error (bp)",12}");
    Console.WriteLine($"  {new string('─', 50)}");

    double[] trueVols = [0.10, 0.15, 0.20, 0.25, 0.30, 0.35, 0.40];
    foreach (double vol in trueVols)
    {
        var market    = MarketHelpers.CreateFlatMarket(valuationDate, 100m, vol, 0.05d, 0.00d);
        decimal price = pricer.Price(option, market).Price;
        var request   = new ImpliedVolatilityRequest(option, guess, price, 0.15d);
        var result    = solver.Solve(request);
        double recovered = result.ImpliedVolatility ?? double.NaN;
        double errBp     = (recovered - vol) * 10000.0;
        Console.WriteLine($"  {vol * 100,7:F1}%  {price,10:F4}  {recovered * 100,12:F6}%  {errBp,+11:F4}");
    }
}

static void Example07SmileSliceConstruction()
{
    Console.WriteLine();
    Console.WriteLine("§8.5  Volatility smile — market quotes and arbitrage-free interpolation");
    Console.WriteLine("      T=6M; quotes at 5 strikes; typical equity skew shape (convex with left tilt)");
    Console.WriteLine();

    double maturity = 0.5d;
    SurfacePoint[] quotes =
    [
        new(maturity,  80m, 0.28d),
        new(maturity,  90m, 0.24d),
        new(maturity, 100m, 0.20d),
        new(maturity, 110m, 0.22d),
        new(maturity, 120m, 0.26d),
    ];

    var slice     = new SmileSliceBuilder().Build(maturity, quotes);
    var surface   = new VolatilitySurface([slice]);
    var arbResult = new QuoteArbitrageValidator().Validate(surface);

    Console.WriteLine($"  Market quotes (T={maturity:F1}):");
    Console.WriteLine($"  {"Strike",8}  {"σ (quoted)",12}  {"σ (surface)",13}");
    Console.WriteLine($"  {new string('─', 38)}");
    foreach (var q in quotes)
        Console.WriteLine($"  {q.Strike,8:F0}  {q.ImpliedVolatility * 100,11:F2}%  {surface.GetVol(q.Strike, maturity) * 100,12:F4}%");

    Console.WriteLine();
    Console.WriteLine("  Interpolated at intermediate strikes:");
    Console.WriteLine($"  {"Strike",8}  {"σ",12}");
    Console.WriteLine($"  {new string('─', 24)}");
    decimal[] interp = [85m, 95m, 105m, 115m];
    foreach (decimal k in interp)
        Console.WriteLine($"  {k,8:F0}  {surface.GetVol(k, maturity) * 100,11:F4}%");

    Console.WriteLine();
    Console.WriteLine($"  Arbitrage-free: {arbResult.IsValid}   (calendar + butterfly spread checks)");
}

static void Example09DermanKaniImpliedTree()
{
    Console.WriteLine();
    Console.WriteLine("§9.4  Derman-Kani implied volatility tree — calibrate local vols from smile");
    Console.WriteLine("      S=100, r=5%, q=0%, T=6M; smile: σ(80)=28%, σ(100)=20%, σ(120)=26%");
    Console.WriteLine();

    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    var market  = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, 0.00d);
    double mat  = 0.5d;
    SurfacePoint[] quotes =
    [
        new(mat,  80m, 0.28d),
        new(mat,  90m, 0.24d),
        new(mat, 100m, 0.20d),
        new(mat, 110m, 0.22d),
        new(mat, 120m, 0.26d),
    ];
    var surface    = new VolatilitySurfaceBuilder().Build(quotes);
    int steps      = 30;
    var calibInput = new ImpliedVolatilityTreeCalibrationInput(surface, steps, market);
    var dkResult   = new ImpliedVolatilityTreeCalibrator().Calibrate(calibInput);

    Console.WriteLine($"  Calibration: {steps} steps,  success: {dkResult.IsSuccess}");
    Console.WriteLine();

    if (dkResult.LocalVolatilities.Count > 0)
    {
        int display = Math.Min(5, dkResult.LocalVolatilities.Count);
        Console.WriteLine($"  Local volatilities by time step (first {display} steps shown):");
        Console.WriteLine($"  {"Step",5}  {"Nodes",6}  {"Min σ_loc",11}  {"Max σ_loc",11}  {"Avg σ_loc",11}");
        Console.WriteLine($"  {new string('─', 50)}");
        for (int t = 0; t < display; t++)
        {
            var row = dkResult.LocalVolatilities[t];
            if (row.Count == 0) continue;
            double mn = row.Min();
            double mx = row.Max();
            double av = row.Average();
            Console.WriteLine($"  {t,5}  {row.Count,6}  {mn,11:F4}  {mx,11:F4}  {av,11:F4}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("  DK builds an implied tree whose Arrow-Debreu prices reprice all input smile");
    Console.WriteLine("  quotes exactly; local vol at each node equals the Dupire local vol surface.");
}

static void Example10RubinsteinImpliedBinomialTree()
{
    Console.WriteLine();
    Console.WriteLine("§10.3 Rubinstein implied binomial tree — fit risk-neutral density to smile");
    Console.WriteLine("      S=100, r=5%, q=0%, T=6M; same smile as §9.4");
    Console.WriteLine();

    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    var market  = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, 0.00d);
    double mat  = 0.5d;
    SurfacePoint[] quotes =
    [
        new(mat,  80m, 0.28d),
        new(mat,  90m, 0.24d),
        new(mat, 100m, 0.20d),
        new(mat, 110m, 0.22d),
        new(mat, 120m, 0.26d),
    ];
    var surface    = new VolatilitySurfaceBuilder().Build(quotes);
    int steps      = 30;
    var calibInput = new ImpliedVolatilityTreeCalibrationInput(surface, steps, market);
    var rubResult  = new ImpliedBinomialTreeCalibrator().Calibrate(calibInput);

    Console.WriteLine($"  Calibration: {steps} steps,  success: {rubResult.IsSuccess}");
    Console.WriteLine();

    if (rubResult.LocalVolatilities.Count > 0)
    {
        int display = Math.Min(5, rubResult.LocalVolatilities.Count);
        Console.WriteLine($"  Local volatilities by time step (first {display} steps shown):");
        Console.WriteLine($"  {"Step",5}  {"Nodes",6}  {"Min σ_loc",11}  {"Max σ_loc",11}  {"Avg σ_loc",11}");
        Console.WriteLine($"  {new string('─', 50)}");
        for (int t = 0; t < display; t++)
        {
            var row = rubResult.LocalVolatilities[t];
            if (row.Count == 0) continue;
            double mn = row.Min();
            double mx = row.Max();
            double av = row.Average();
            Console.WriteLine($"  {t,5}  {row.Count,6}  {mn,11:F4}  {mx,11:F4}  {av,11:F4}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("  Rubinstein fits terminal node probabilities to match input option prices;");
    Console.WriteLine("  the tree is recombining with state-dependent transition probabilities.");
}

static void Example09BarrierOptionFlatVol()
{
    Console.WriteLine();
    Console.WriteLine("§11.3 Barrier option pricing under flat volatility (200-step CRR tree)");
    Console.WriteLine("      S=100, K=100, T=6M, r=5%, σ=20%, q=1%");
    Console.WriteLine("      In-out parity: KI + KO = vanilla");
    Console.WriteLine();

    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    DateOnly expiry = valuationDate.AddMonths(6);
    var market  = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, 0.01d);
    var input   = TreeModelInput.FromMarketData(market, 100m, expiry, 200);
    var tree    = new CoxRossRubinsteinTreeBuilder().Build(input);
    var barrier = new BarrierTreePricer();
    var vanilla = new EuropeanTreePricer();

    var callVanilla = vanilla.Price(new EuropeanOption(CallPut.Call, 100m, expiry), market, tree).Price;
    var putVanilla  = vanilla.Price(new EuropeanOption(CallPut.Put,  100m, expiry), market, tree).Price;

    Console.WriteLine($"  {"Option",22}  {"Barrier",8}  {"KI",8}  {"KO",8}  {"KI+KO",8}  {"Vanilla",8}");
    Console.WriteLine($"  {new string('─', 70)}");

    var families = new (BarrierDirection dir, CallPut cp, decimal bar, decimal van)[]
    {
        (BarrierDirection.Up,   CallPut.Call, 120m, callVanilla),
        (BarrierDirection.Down, CallPut.Call,  80m, callVanilla),
        (BarrierDirection.Up,   CallPut.Put,  120m, putVanilla),
        (BarrierDirection.Down, CallPut.Put,   80m, putVanilla),
    };

    foreach (var (dir, cp, bar, van) in families)
    {
        var ki  = new BarrierOption(cp, 100m, bar, dir, BarrierKnockType.In,  expiry);
        var ko  = new BarrierOption(cp, 100m, bar, dir, BarrierKnockType.Out, expiry);
        decimal kiP = barrier.Price(ki, market, tree).Price;
        decimal koP = barrier.Price(ko, market, tree).Price;
        string label = $"{dir}-{cp} H={bar}";
        Console.WriteLine($"  {label,22}  {bar,8:F0}  {kiP,8:F4}  {koP,8:F4}  {kiP+koP,8:F4}  {van,8:F4}");
    }
}

static void Example10BarrierOptionSmileAware()
{
    Console.WriteLine();
    Console.WriteLine("§11.5 Smile-aware vs flat-vol barrier pricing — skew impact on knock-out options");
    Console.WriteLine("      S=100, K=100, T=6M, r=5%, σ_ATM=20%, q=1%; smile: σ(80)=28%, σ(120)=26%");
    Console.WriteLine();

    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    DateOnly expiry        = valuationDate.AddMonths(6);
    double maturity        = 0.5d;
    var market = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, 0.01d);

    SurfacePoint[] quotes =
    [
        new(maturity,  80m, 0.28d),
        new(maturity,  90m, 0.24d),
        new(maturity, 100m, 0.20d),
        new(maturity, 110m, 0.22d),
        new(maturity, 120m, 0.26d),
    ];
    var surface = new VolatilitySurfaceBuilder().Build(quotes);

    int steps      = 50;
    var flatInput  = TreeModelInput.FromMarketData(market, 100m, expiry, steps);
    var flatTree   = new CoxRossRubinsteinTreeBuilder().Build(flatInput);
    var flatPricer = new BarrierTreePricer();
    var calibInput = new ImpliedVolatilityTreeCalibrationInput(surface, steps, market);
    var calibResult = new ImpliedVolatilityTreeCalibrator().Calibrate(calibInput);
    var smilePricer = new SmileAwareBarrierPricer();

    var barriers = new (string label, BarrierOption option)[]
    {
        ("Down-Out Call H=80",  new BarrierOption(CallPut.Call, 100m,  80m, BarrierDirection.Down, BarrierKnockType.Out, expiry)),
        ("Up-Out   Call H=120", new BarrierOption(CallPut.Call, 100m, 120m, BarrierDirection.Up,   BarrierKnockType.Out, expiry)),
        ("Down-Out Put  H=80",  new BarrierOption(CallPut.Put,  100m,  80m, BarrierDirection.Down, BarrierKnockType.Out, expiry)),
        ("Up-Out   Put  H=120", new BarrierOption(CallPut.Put,  100m, 120m, BarrierDirection.Up,   BarrierKnockType.Out, expiry)),
    };

    Console.WriteLine($"  {"Option",22}  {"Flat-vol",10}  {"Smile-aware",12}  {"Skew impact",12}");
    Console.WriteLine($"  {new string('─', 62)}");

    foreach (var (label, opt) in barriers)
    {
        decimal flatP  = flatPricer.Price(opt, market, flatTree).Price;
        decimal smileP = smilePricer.Price(opt, market, calibResult).Price;
        Console.WriteLine($"  {label,22}  {flatP,10:F4}  {smileP,12:F4}  {smileP - flatP,+12:F4}");
    }

    Console.WriteLine();
    Console.WriteLine("  Down-out calls near a low barrier are cheapened by the smile skew:");
    Console.WriteLine("  higher σ at low strikes raises the probability of knockout, lowering value.");
}

static void Example16DeltaHedgingSimulation()
{
    Console.WriteLine();
    Console.WriteLine("§5.3  Delta-hedging P&L simulation — hedging error distribution over 5 000 GBM paths");
    Console.WriteLine("      S=100, K=100, T=6M, r=5%, σ=20%, q=0%;  126 daily rebalance steps");

    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    var option = new EuropeanOption(CallPut.Call, 100m, valuationDate.AddMonths(6));
    var market = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, 0.00d);

    double sigma = market.FlatVolatility;
    double r = market.RiskFreeRate(option.ExpiryDate);
    double T = market.TimeToExpiryInYears(option.ExpiryDate);

    decimal bsPrice = new BlackScholesPricer().Price(option, market).Price;
    Console.WriteLine($"Example 16 | BS price: {bsPrice:F4}");

    var simulator = new DeltaHedgeSimulator();
    int pathCount = 5000;
    int rebalanceSteps = 126;
    double dt = T / rebalanceSteps;
    var rng = new Random(42);
    var errors = new List<double>(pathCount);

    for (int path = 0; path < pathCount; path++)
    {
        var spots = new List<decimal>(rebalanceSteps + 1) { market.Spot };
        double currentSpot = (double)market.Spot;
        for (int step = 0; step < rebalanceSteps; step++)
        {
            double z = SimulationHelpers.NormalSample(rng);
            currentSpot *= Math.Exp((r - sigma * sigma / 2d) * dt + sigma * Math.Sqrt(dt) * z);
            spots.Add((decimal)currentSpot);
        }

        var request = new DeltaHedgeSimulationRequest(option, market, spots);
        var result = simulator.Simulate(request);
        errors.Add((double)result.HedgingError);
    }

    double meanError = errors.Average();
    double stdError = Math.Sqrt(errors.Average(e => e * e) - meanError * meanError);

    Console.WriteLine($"Example 16 | Paths: {pathCount}, rebalance steps: {rebalanceSteps}");
    Console.WriteLine($"Example 16 | Mean hedging error:  {meanError:F4}");
    Console.WriteLine($"Example 16 | Std dev of error:    {stdError:F4}");
    Console.WriteLine($"Example 16 | Error as % of price: {stdError / (double)bsPrice * 100d:F2}%");
}

static void Example17ArrowDebreuStatePrices()
{
    Console.WriteLine();
    Console.WriteLine("§6.11 Arrow-Debreu state prices — each terminal node price = discounted risk-neutral prob");
    Console.WriteLine("      S=100, T=6M, r=5%, σ=20%, q=0%;  10-step CRR tree");

    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    var market = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, 0.00d);
    DateOnly expiry = valuationDate.AddMonths(6);
    double r = market.RiskFreeRate(expiry);
    double T = market.TimeToExpiryInYears(expiry);

    int steps = 10;
    var input = TreeModelInput.FromMarketData(market, 100m, expiry, steps);
    var tree = new CoxRossRubinsteinTreeBuilder().Build(input);

    var calculator = new ArrowDebreuStatePriceCalculator();
    var statePrices = calculator.CalculateTerminalStatePrices(tree, r);

    double expectedDf = Math.Exp(-r * T);
    double priceSum = statePrices.Sum();

    Console.WriteLine($"Example 17 | Tree: {steps} steps, {statePrices.Count} terminal nodes");
    Console.WriteLine($"Example 17 | State prices (node → spot → AD price):");
    for (int j = 0; j < statePrices.Count; j++)
    {
        double spot = tree.TerminalSpots[j];
        Console.WriteLine($"             [{j,2}] S={spot,8:F2}  AD={statePrices[j]:F6}");
    }

    Console.WriteLine($"Example 17 | Sum of state prices:   {priceSum:F6}");
    Console.WriteLine($"Example 17 | Discount factor e^-rT: {expectedDf:F6}");
    Console.WriteLine($"Example 17 | Difference:            {priceSum - expectedDf:E2}");

    int midNode = statePrices.Count / 2;
    double midSpot = tree.TerminalSpots[midNode];
    Console.WriteLine($"Example 17 | Butterfly at S={midSpot:F2}: AD price = {statePrices[midNode]:F6}");
}

static void Example18DividendImpact()
{
    Console.WriteLine();
    Console.WriteLine("§4.9  Dividend impact on call price and delta");
    Console.WriteLine("      S=100, K=100, T=6M, r=5%, σ=20%;  continuous dividend yield q varies");
    Console.WriteLine();

    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    DateOnly expiry = valuationDate.AddMonths(6);
    var option = new EuropeanOption(CallPut.Call, 100m, expiry);
    var pricer = new BlackScholesPricer();
    var greekProvider = new BlackScholesGreeks();

    double[] dividendYields = [0.00d, 0.02d, 0.04d, 0.06d, 0.08d];

    Console.WriteLine($"  {"q",8}  {"Forward",10}  {"Call Price",12}  {"Delta",8}");
    Console.WriteLine($"  {new string('─', 45)}");

    foreach (double q in dividendYields)
    {
        var market = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, q);
        var result = pricer.Price(option, market);
        var greeks = greekProvider.CalculateGreeks(option, market);
        double fwd = market.ForwardPrice(expiry);

        Console.WriteLine($"  {q * 100d,7:F1}%  {fwd,10:F4}  {result.Price,12:F4}  {greeks.Delta,8:F4}");
    }
}

static void Example19DigitalOptionOnTree()
{
    Console.WriteLine();
    Console.WriteLine("§7.7  Digital (cash-or-nothing) call — tree vs closed form e^{−rT}·N(d₂)");
    Console.WriteLine("      S=100, K=100, T=6M, r=5%, σ=20%, q=0%");
    Console.WriteLine();

    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    decimal strike = 100m;
    DateOnly expiry = valuationDate.AddMonths(6);
    var market = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, 0.00d);

    double S = (double)market.Spot;
    double K = (double)strike;
    double sigma = market.FlatVolatility;
    double r = market.RiskFreeRate(expiry);
    double T = market.TimeToExpiryInYears(expiry);

    double d2 = (Math.Log(S / K) + (r - sigma * sigma / 2d) * T) / (sigma * Math.Sqrt(T));
    double closedForm = Math.Exp(-r * T) * CumulativeNormal.Evaluate(d2);

    Console.WriteLine($"  {"Steps",7}  {"Tree Price",12}  {"Closed Form",13}  {"Error (bps)",13}");
    Console.WriteLine($"  {new string('─', 52)}");

    int[] stepCounts = [50, 100, 200, 500];
    var treeBuilder = new CoxRossRubinsteinTreeBuilder();

    foreach (int steps in stepCounts)
    {
        var input = TreeModelInput.FromMarketData(market, strike, expiry, steps);
        var tree = treeBuilder.Build(input);
        double p = tree.RiskNeutralProbability;
        double discount = Math.Exp(-r * tree.DeltaTime);

        int terminalNodes = tree.Steps + 1;
        double[] values = new double[terminalNodes];
        for (int j = 0; j < terminalNodes; j++)
            values[j] = tree.TerminalSpots[j] > K ? 1d : 0d;

        for (int t = tree.Steps - 1; t >= 0; t--)
        {
            double[] newValues = new double[t + 1];
            for (int j = 0; j <= t; j++)
                newValues[j] = discount * (p * values[j] + (1d - p) * values[j + 1]);
            values = newValues;
        }

        double treePrice = values[0];
        double errorBps = (treePrice - closedForm) / closedForm * 10000d;
        Console.WriteLine($"  {steps,7}  {treePrice,12:F6}  {closedForm,13:F6}  {errorBps,+13:F2}");
    }
}

static void Example20BarrierInOutParity()
{
    Console.WriteLine();
    Console.WriteLine("§11.2 Barrier in-out parity:  KI + KO = vanilla  (500-step CRR tree)");

    DateOnly valuationDate = DateOnly.FromDateTime(DateTime.Today);
    var market = MarketHelpers.CreateFlatMarket(valuationDate, 100m, 0.20d, 0.05d, 0.01d);
    DateOnly expiry = valuationDate.AddMonths(6);

    int steps = 500;
    var input = TreeModelInput.FromMarketData(market, 100m, expiry, steps);
    var tree = new CoxRossRubinsteinTreeBuilder().Build(input);
    var barrierPricer = new BarrierTreePricer();
    var vanillaPricer = new EuropeanTreePricer();

    var families = new (BarrierDirection dir, CallPut cp, decimal barrier)[]
    {
        (BarrierDirection.Up, CallPut.Call, 120m),
        (BarrierDirection.Down, CallPut.Call, 80m),
        (BarrierDirection.Up, CallPut.Put, 120m),
        (BarrierDirection.Down, CallPut.Put, 80m)
    };

    Console.WriteLine($"  {steps}-step tree, S=100, K=100, r=5%, σ=20%, q=1%");
    Console.WriteLine();
    Console.WriteLine($"  {"Family",17}  {"KI Price",10}  {"KO Price",10}  {"KI+KO",10}  {"Vanilla",10}  {"Parity Err",10}");
    Console.WriteLine($"  {new string('─', 72)}");

    foreach (var (dir, cp, barrier) in families)
    {
        var vanilla = cp == CallPut.Call
            ? new EuropeanOption(CallPut.Call, 100m, expiry)
            : new EuropeanOption(CallPut.Put, 100m, expiry);
        var vanillaPrice = vanillaPricer.Price(vanilla, market, tree).Price;

        var kiOption = new BarrierOption(cp, 100m, barrier, dir, BarrierKnockType.In, expiry);
        var koOption = new BarrierOption(cp, 100m, barrier, dir, BarrierKnockType.Out, expiry);
        var kiPrice = barrierPricer.Price(kiOption, market, tree).Price;
        var koPrice = barrierPricer.Price(koOption, market, tree).Price;
        decimal parityError = kiPrice + koPrice - vanillaPrice;

        string label = $"{dir}-{cp} H={barrier}";
        Console.WriteLine($"  {label,-17}  {kiPrice,10:F4}  {koPrice,10:F4}  {kiPrice + koPrice,10:F4}  {vanillaPrice,10:F4}  {parityError,+10:F6}");
    }
}

static void Chapter01_SecurityPayoffDiagrams()
{
    Console.WriteLine();
    Console.WriteLine("Chapter 01 | Stocks, Options, and Futures (Black-Scholes and Beyond, Ch. 1)");

    const double K_payoff = 100.0;
    const double premium  = 5.0;

    Console.WriteLine();
    Console.WriteLine("§1.7  Payoff at expiry  (K=100, option premium=5)");
    Console.WriteLine($"  {"S_T",6} {"LongCall",10} {"ShortCall",10} {"LongPut",9} {"ShortPut",9} {"LongStock",10}");
    Console.WriteLine($"  {new string('─', 60)}");
    double[] spots = { 70, 80, 90, 95, 100, 105, 110, 120, 130 };
    foreach (double s in spots)
    {
        double lc  =  Math.Max(s - K_payoff, 0) - premium;
        double sc  = -Math.Max(s - K_payoff, 0) + premium;
        double lp  =  Math.Max(K_payoff - s, 0) - premium;
        double sp  = -Math.Max(K_payoff - s, 0) + premium;
        double ls  = s - K_payoff;
        Console.WriteLine($"  {s,6:F0} {lc,10:F2} {sc,10:F2} {lp,9:F2} {sp,9:F2} {ls,10:F2}");
    }

    Console.WriteLine();
    Console.WriteLine("§1.9  Put-call parity   C − P = S − e^{−r(T−t)}·K");
    Console.WriteLine("      Book example: C=$8.60, P=$3.50, S=K=$100, r=5%, T−t=1 yr");
    Console.WriteLine();
    const double C   = 8.60;
    const double P   = 3.50;
    const double S   = 100.0;
    const double K   = 100.0;
    const double r   = 0.05;
    const double Tt  = 1.0;
    double cMinusP   = C - P;
    double pvK       = Math.Exp(-r * Tt) * K;
    double sMinusPvK = S - pvK;
    double mispricing = cMinusP - sMinusPvK;
    Console.WriteLine($"  C − P               = {C:F2} − {P:F2} = {cMinusP:F2}");
    Console.WriteLine($"  PV(K) = e^(−0.05)×100  = {pvK:F4}");
    Console.WriteLine($"  S − PV(K)           = {sMinusPvK:F4}");
    Console.WriteLine($"  Mispricing          = {mispricing:F4}   (book: $0.22)");
    Console.WriteLine();
    Console.WriteLine("  Arbitrage portfolio:");
    Console.WriteLine($"    (1) Sell call      +{C:F2}");
    Console.WriteLine($"    (2) Buy  put       −{P:F2}   net = {cMinusP:F2}");
    Console.WriteLine($"    (3) Borrow PV(K)   +{pvK:F4}  total = {cMinusP + pvK:F4}");
    Console.WriteLine($"    (4) Buy  stock     −{S:F2}  riskless profit = {cMinusP + pvK - S:F4}");

    Console.WriteLine();
    Console.WriteLine("§1.11 American call on non-dividend stock: never early exercise");
    Console.WriteLine("  Selling always dominates: C_E ≥ S − PV(K) ≥ S − K (intrinsic)");
    Console.WriteLine("  Early exercise forfeits time value + interest on K.");
    Console.WriteLine("  Exception: just before ex-dividend date when D > K(1−e^{−rδt}).");

    Console.WriteLine();
    Console.WriteLine("§1.12 Forward contract value  C_F = PV(S component) − e^{−r(T−t)}·F");
    Console.WriteLine("      Display 1.11.1 — three underlying types");

    const double q  = 0.02;
    const double rf = 0.03;
    const double F  = 102.0;

    double fwdStock = Math.Exp((r - q) * Tt) * S;
    double cFStock  = Math.Exp(-q * Tt) * S - Math.Exp(-r * Tt) * F;
    Console.WriteLine();
    Console.WriteLine($"  [Stock/index, q={q*100:F0}% continuous yield, F={F}]");
    Console.WriteLine($"    Fair forward  = e^(r−q)(T−t)·S = {fwdStock:F4}");
    Console.WriteLine($"    C_F           = e^(−q·Δt)·S − e^(−r·Δt)·F = {cFStock:F4}");

    const double D  = 3.0;
    const double tD = 0.5;
    double pvD      = Math.Exp(-r * tD) * D;
    double fwdDiv   = Math.Exp(r * Tt) * (S - pvD);
    double cFDiv    = S - pvD - Math.Exp(-r * Tt) * F;
    Console.WriteLine();
    Console.WriteLine($"  [Stock + lumpy dividend D={D} at t={tD} yr, F={F}]");
    Console.WriteLine($"    PV(D)         = e^(−r·t_D)·D = {pvD:F4}");
    Console.WriteLine($"    Fair forward  = e^(r·Δt)·(S−PV(D)) = {fwdDiv:F4}");
    Console.WriteLine($"    C_F           = S − PV(D) − e^(−r·Δt)·F = {cFDiv:F4}");

    const double Sfx = 1.25;
    const double Ffx = 1.22;
    double fwdFx = Math.Exp((r - rf) * Tt) * Sfx;
    double cFFx  = Math.Exp(-rf * Tt) * Sfx - Math.Exp(-r * Tt) * Ffx;
    Console.WriteLine();
    Console.WriteLine($"  [Currency, spot={Sfx}, r_f={rf*100:F0}%, F={Ffx}]");
    Console.WriteLine($"    Fair forward  = e^(r−r_f)(T−t)·S = {fwdFx:F4}");
    Console.WriteLine($"    C_F           = e^(−r_f·Δt)·S − e^(−r·Δt)·F = {cFFx:F4}");
}

static void Chapter02_NormalDistributionProperties()
{
    Console.WriteLine();
    Console.WriteLine("Chapter 02 | Fundamental Mathematical Concepts (Black-Scholes and Beyond, Ch. 2)");

    Console.WriteLine();
    Console.WriteLine("§2.1  Table 2.1.1  Compound interest: (1 + r/n)^n → e^r   (r = 5%)");
    Console.WriteLine();
    const double r = 0.05;
    Console.WriteLine($"  {"n",8}  {"(1+r/n)^n",16}");
    Console.WriteLine($"  {new string('─', 28)}");
    int[] ns = { 1, 10, 50, 100, 500, 1000, 10000, 1_000_000 };
    foreach (int n in ns)
        Console.WriteLine($"  {n,8}  {Math.Pow(1.0 + r / n, n),16:F10}");
    Console.WriteLine($"  {"e^r",8}  {Math.Exp(r),16:F10}");

    Console.WriteLine();
    Console.WriteLine("§2.1  Display 2.1.1  Rate conversions   r_cont = n·ln(1+r1/n),  r1_n = n·(e^{r/n}−1)");
    Console.WriteLine();
    Console.WriteLine($"  {"Period",22} {"n",6} {"Continuous",12} {"n-period",12}");
    Console.WriteLine($"  {new string('─', 54)}");
    (int nv, string label)[] cases =
    {
        (1,   "Annual"),
        (2,   "Semi-annual"),
        (4,   "Quarterly"),
        (12,  "Monthly"),
        (365, "Daily"),
    };
    foreach (var (nv, label) in cases)
    {
        double r1    = 0.05;
        double rCont = nv * Math.Log(1.0 + r1 / nv);
        double rBack = nv * (Math.Exp(rCont / nv) - 1.0);
        Console.WriteLine($"  {label,22} {nv,6} {rCont,12:F8} {rBack,12:F8}");
    }

    Console.WriteLine();
    Console.WriteLine("§2.5  Four formulas for N(x) — Displays 2.5.1 and 2.5.2");
    Console.WriteLine("  Poly1    : 3-term polynomial, ±5×10⁻⁴  (Display 2.5.1)");
    Console.WriteLine("  Poly2    : 5-term polynomial, ±7.5×10⁻⁸ (Display 2.5.1)");
    Console.WriteLine("  Rational1: 4-term rational,  ±2.5×10⁻⁴  (Display 2.5.2)");
    Console.WriteLine("  Rational2: 6-term rational,  ±1×10⁻⁷    (Display 2.5.2)");
    Console.WriteLine("  Exact    : Boutquin.Numerics CumulativeNormal (Laikov 2025, ~14 sig. digits)");
    Console.WriteLine();
    Console.WriteLine($"  {"x",5} {"Poly1",10} {"Poly2",10} {"Rational1",10} {"Rational2",10} {"Exact",10}");
    Console.WriteLine($"  {new string('─', 59)}");
    double[] xs = { -2.0, -1.5, -1.0, -0.5, 0.0, 0.5, 1.0, 1.5, 2.0 };
    foreach (double x in xs)
    {
        Console.WriteLine($"  {x,5:F1} {BookPoly1(x),10:F6} {BookPoly2(x),10:F6}" +
                          $" {BookRational1(x),10:F6} {BookRational2(x),10:F6}" +
                          $" {CumulativeNormal.Evaluate(x),10:F6}");
    }

    Console.WriteLine();
    Console.WriteLine("§2.6  Properties of N(x)");
    Console.WriteLine($"  N(0)        = {CumulativeNormal.Evaluate(0.0):F6}  (should be 0.500000)");
    Console.WriteLine($"  N(1)+N(−1)  = {CumulativeNormal.Evaluate(1.0) + CumulativeNormal.Evaluate(-1.0):F6}  (should be 1.000000)");
    Console.WriteLine($"  N(2)+N(−2)  = {CumulativeNormal.Evaluate(2.0) + CumulativeNormal.Evaluate(-2.0):F6}  (should be 1.000000)");
}

static void Chapter03_GbmPriceDistribution()
{
    Console.WriteLine();
    Console.WriteLine("Chapter 03 | The Geometric Brownian Motion Model (Black-Scholes and Beyond, Ch. 3)");

    Console.WriteLine();
    Console.WriteLine("§3.3  Table 3.3.1 — Daily prices (μ=8%, σ=20%) with log-returns");
    double[] prices =
    {
        50.00, 50.79, 49.78, 49.12, 48.67, 48.94, 48.69, 49.14, 49.30, 48.68,
        48.78, 48.33, 47.97, 48.83, 47.82, 46.62, 46.93, 46.02, 45.95, 46.11,
    };
    double dt = 1.0 / 365.0;

    Console.WriteLine($"  {"Day",4}  {"Price",8}  {"r_i = log(S_i/S_{i-1})",24}");
    Console.WriteLine($"  {new string('─', 42)}");
    double[] logReturns = new double[prices.Length - 1];
    for (int i = 0; i < prices.Length; i++)
    {
        if (i == 0)
            Console.WriteLine($"  {i+1,4}  {prices[i],8:F2}  {"—",24}");
        else
        {
            logReturns[i-1] = Math.Log(prices[i] / prices[i-1]);
            Console.WriteLine($"  {i+1,4}  {prices[i],8:F2}  {logReturns[i-1],24:F6}");
        }
    }

    int N         = logReturns.Length;
    double rBar   = logReturns.Average();
    double varEst = logReturns.Sum(ri => (ri - rBar) * (ri - rBar)) / N;
    double sigEst = Math.Sqrt(varEst / dt);

    Console.WriteLine();
    Console.WriteLine("§3.3  5-step calibration (Δt = 1/365, daily):");
    Console.WriteLine($"  Step 1: Δt = 1/365 = {dt:F6}");
    Console.WriteLine($"  Step 2: {prices.Length} prices → N = {N} log-returns");
    Console.WriteLine("  Step 3: r_i = log(S_{i+1}/S_i)  — see table above");
    Console.WriteLine($"  Step 4: r̄   = (1/N)·Σr_i = {rBar:F6}   ({rBar*100:F2}%)  [book: −0.0043]");
    Console.WriteLine($"  Step 5: σ̂   = (1/√Δt)·√((1/N)·Σ(r_i−r̄)²) = {sigEst:F4}  ({sigEst*100:F2}%)  [book: 24.07%]");
    Console.WriteLine($"          True σ = 20%.  Small-sample error is expected (N=19 returns).");

    Console.WriteLine();
    Console.WriteLine("§3.4  GBM log-price distribution:  log(S_T/S_0) ~ N((μ−σ²/2)·Δt,  σ²·Δt)");
    const double mu    = 0.08;
    const double sigma = 0.20;
    const double S0    = 50.0;
    double horizon     = 1.0;
    double meanLog  = (mu - sigma * sigma / 2.0) * horizon;
    double stdLog   = sigma * Math.Sqrt(horizon);
    Console.WriteLine();
    Console.WriteLine($"  S_0={S0},  μ={mu*100:F0}%,  σ={sigma*100:F0}%,  T−t={horizon:F1} yr");
    Console.WriteLine($"  Mean  log-return = (μ−σ²/2)·(T−t) = {meanLog:F4}");
    Console.WriteLine($"  Std   log-return = σ·√(T−t)       = {stdLog:F4}");
    Console.WriteLine();
    Console.WriteLine("  P(S_T ≥ K) = N(d₂)   where  d₂ = (log(S_0/K) + (μ−σ²/2)(T−t)) / (σ√(T−t))");
    Console.WriteLine();
    Console.WriteLine($"  {"K",8} {"d₂",8} {"P(S_T≥K)",12} {"E[S_T·1_{S_T≥K}]",18}");
    Console.WriteLine($"  {new string('─', 50)}");
    double[] strikes = { 40.0, 45.0, 50.0, 55.0, 60.0, 65.0, 70.0 };
    foreach (double kk in strikes)
    {
        double d2   = (Math.Log(S0 / kk) + meanLog) / stdLog;
        double d1   = d2 + stdLog;
        double prob = CumulativeNormal.Evaluate(d2);
        double eSt  = S0 * Math.Exp(mu * horizon) * CumulativeNormal.Evaluate(d1);
        Console.WriteLine($"  {kk,8:F0} {d2,8:F4} {prob,12:F6} {eSt,18:F4}");
    }
    double eStFull = S0 * Math.Exp(mu * horizon);
    Console.WriteLine();
    Console.WriteLine($"  E[S_T] = S_0·e^(μ·(T−t)) = {S0}·e^{mu*horizon:F4} = {eStFull:F4}");
}

// Book N(x) approximations reproducing the text formulas from Ch. 2.
// Intentionally kept at their historical precision — not replacements for CumulativeNormal.Evaluate.

static double BookPoly1(double x)
{
    bool neg = x < 0;
    double ax = Math.Abs(x);
    const double p  =  0.33267;
    const double a1 =  0.4361836;
    const double a2 = -0.1201676;
    const double a3 =  0.9372980;
    double t   = 1.0 / (1.0 + p * ax);
    double z   = Math.Exp(-ax * ax / 2.0) / Math.Sqrt(2.0 * Math.PI);
    double res = 1.0 - z * t * (a1 + t * (a2 + t * a3));
    return neg ? 1.0 - res : res;
}

static double BookPoly2(double x)
{
    bool neg = x < 0;
    double ax = Math.Abs(x);
    const double p  =  0.2316419;
    const double b1 =  0.319381530;
    const double b2 = -0.356563782;
    const double b3 =  1.781477937;
    const double b4 = -1.821255978;
    const double b5 =  1.330274429;
    double t   = 1.0 / (1.0 + p * ax);
    double z   = Math.Exp(-ax * ax / 2.0) / Math.Sqrt(2.0 * Math.PI);
    double res = 1.0 - z * t * (b1 + t * (b2 + t * (b3 + t * (b4 + t * b5))));
    return neg ? 1.0 - res : res;
}

static double BookRational1(double x)
{
    bool neg = x < 0;
    double ax = Math.Abs(x);
    const double c1 = 0.196854;
    const double c2 = 0.115194;
    const double c3 = 0.000344;
    const double c4 = 0.019527;
    double inner = 1.0 + ax * (c1 + ax * (c2 + ax * (c3 + ax * c4)));
    double res   = 1.0 - 0.5 * Math.Pow(inner, -4);
    return neg ? 1.0 - res : res;
}

static double BookRational2(double x)
{
    bool neg = x < 0;
    double ax = Math.Abs(x);
    const double d1 = 0.0498673470;
    const double d2 = 0.0211410061;
    const double d3 = 0.0032776263;
    const double d4 = 0.0000380036;
    const double d5 = 0.0000488906;
    const double d6 = 0.0000053830;
    double inner = 1.0 + ax * (d1 + ax * (d2 + ax * (d3 + ax * (d4 + ax * (d5 + ax * d6)))));
    double res   = 1.0 - 0.5 * Math.Pow(inner, -16);
    return neg ? 1.0 - res : res;
}

// ── Types ─────────────────────────────────────────────────────────────────────

sealed record ChapterInfo(int Number, string Title, List<ExampleEntry> Examples);
sealed record ExampleEntry(string Id, string Description, Func<Task> Run);
