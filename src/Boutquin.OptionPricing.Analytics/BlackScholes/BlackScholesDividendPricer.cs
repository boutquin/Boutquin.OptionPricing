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

using Boutquin.OptionPricing.Abstractions;
using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Market;
using Boutquin.OptionPricing.Domain.Results;

namespace Boutquin.OptionPricing.Analytics.BlackScholes;

/// <summary>
/// Convenience wrapper that prices a European option with an explicit dividend yield
/// by delegating to <see cref="BlackScholesPricer"/>.
/// </summary>
/// <remarks>
/// The continuous dividend yield is already embedded in
/// <see cref="OptionMarketData.DividendYield"/>; this class exists as a named
/// entry point for callers who want to emphasize dividend-adjusted pricing.
/// Internally it forwards every call to <see cref="BlackScholesPricer.Price"/>.
/// </remarks>
public sealed class BlackScholesDividendPricer : IOptionPricer<EuropeanOption, OptionMarketData, PriceResult>
{
    private readonly BlackScholesPricer _pricer = new();

    /// <summary>
    /// Computes the dividend-adjusted Black-Scholes fair value for a European option.
    /// </summary>
    /// <param name="option">European option contract specifying strike, expiry, and call/put flag.</param>
    /// <param name="market">
    /// Market snapshot whose dividend curve supplies the continuous yield used in the
    /// Generalized Black-Scholes formula.
    /// </param>
    /// <returns>
    /// A <see cref="PriceResult"/> identical to what <see cref="BlackScholesPricer.Price"/>
    /// would return for the same inputs.
    /// </returns>
    public PriceResult Price(EuropeanOption option, OptionMarketData market)
    {
        return _pricer.Price(option, market);
    }
}
