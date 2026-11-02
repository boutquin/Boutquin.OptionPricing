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

using System.Text.Json;
using Boutquin.OptionPricing.Analytics.BlackScholes;
using Boutquin.OptionPricing.Analytics.Parity;
using Boutquin.OptionPricing.Trees.Binomial;
using Boutquin.OptionPricing.Trees.Pricing;

namespace Boutquin.OptionPricing.Tests.Verification;

public sealed class QuantLibRcVerificationTests : CrossLanguageVerificationBase
{
    public static IEnumerable<object[]> Scenarios()
    {
        using var document = LoadVector("quantlib-rc");
        foreach (var row in document.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            yield return [row.GetProperty("id").GetInt32(), row.GetRawText()];
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void PriceAndGreeks_MatchPinnedQuantLibEngine(int id, string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var date = new DateOnly(2026, 4, 9);
        var expiry = date.AddDays(row.GetProperty("days").GetInt32());
        var market = BuildMarket(date, row.GetProperty("spot").GetDecimal(), row.GetProperty("sigma").GetDouble(),
            row.GetProperty("r").GetDouble(), row.GetProperty("q").GetDouble(), row.GetProperty("b").GetDouble());
        var option = BuildEuropeanOption(row.GetProperty("strike").GetDecimal(), expiry, row.GetProperty("call_put").GetString()!);
        var price = new BlackScholesPricer().Price(option, market);
        Assert.True(price.IsSuccess);
        AssertWithin((double)price.Price.Amount, row.GetProperty("price").GetDouble(), 1e-6, $"QuantLib {id} price");
        var greeks = new BlackScholesGreeks().CalculateGreeks(option, market);
        foreach (var (key, actual) in new[] { ("delta", greeks.Delta), ("gamma", greeks.Gamma),
            ("vega", greeks.Vega), ("theta", greeks.Theta), ("rho", greeks.Rho) })
        {
            Assert.NotNull(actual);
            AssertWithin(actual.Value, row.GetProperty(key).GetDouble(), 1e-6, $"QuantLib {id} {key}");
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void CurveBasedParity_MatchesIndependentOppositePrice(int id, string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var date = new DateOnly(2026, 4, 9);
        var expiry = date.AddDays(row.GetProperty("days").GetInt32());
        var spot = row.GetProperty("spot").GetDecimal();
        var strike = row.GetProperty("strike").GetDecimal();
        var market = BuildMarket(date, spot, row.GetProperty("sigma").GetDouble(),
            row.GetProperty("r").GetDouble(), row.GetProperty("q").GetDouble(), row.GetProperty("b").GetDouble());
        var isCall = row.GetProperty("call_put").GetString() == "Call";
        var opposite = BuildEuropeanOption(strike, expiry, isCall ? "Put" : "Call");
        var oppositePrice = new BlackScholesPricer().Price(opposite, market).Price.Amount;
        var implied = isCall ? PutCallParity.ImpliedCallPrice(oppositePrice, spot, strike, market, expiry)
            : PutCallParity.ImpliedPutPrice(oppositePrice, spot, strike, market, expiry);
        AssertWithin((double)implied, row.GetProperty("price").GetDouble(), 1e-6, $"QuantLib {id} parity");
    }
    public static IEnumerable<object[]> TreeScenarios()
    {
        using var document = LoadVector("quantlib-trees-rc");
        foreach (var row in document.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            yield return [row.GetProperty("id").GetInt32(), row.GetRawText()];
        }
    }

    [Theory]
    [MemberData(nameof(TreeScenarios))]
    public void TreePrices_MatchIndependentQuantLibFiniteDifferenceEngine(int id, string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var date = new DateOnly(2026, 4, 9);
        var expiry = date.AddDays(row.GetProperty("days").GetInt32());
        var strike = row.GetProperty("strike").GetDecimal();
        var side = row.GetProperty("call_put").GetString()!;
        var market = BuildMarket(date, row.GetProperty("spot").GetDecimal(), row.GetProperty("sigma").GetDouble(),
            row.GetProperty("r").GetDouble(), row.GetProperty("q").GetDouble(), row.GetProperty("b").GetDouble());
        var tree = new CoxRossRubinsteinTreeBuilder().Build(TreeModelInput.FromMarketData(market, strike, expiry, 512));
        var actual = row.GetProperty("exercise").GetString() == "American"
            ? new AmericanTreePricer().Price(BuildAmericanOption(strike, expiry, side), market, tree)
            : new EuropeanTreePricer().Price(BuildEuropeanOption(strike, expiry, side), market, tree);
        Assert.True(actual.IsSuccess);
        // CRR discretization differs from the independently refined finite-difference engine.
        AssertWithin((double)actual.Price.Amount, row.GetProperty("price").GetDouble(), 0.05, $"QuantLib FD {id}");
    }
}
