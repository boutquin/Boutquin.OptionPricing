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

using Boutquin.OptionPricing.Domain.Market;

namespace Boutquin.OptionPricing.Analytics.Parity;

/// <summary>
/// Implements put-call parity for European options on an underlying with continuous
/// dividend yield: <c>C - P = S * exp(-q*T) - K * exp(-r*T)</c>.
/// Provides methods to recover the implied call or put price from the other leg,
/// and to verify whether an observed call/put pair satisfies the parity within tolerance.
/// </summary>
public static class PutCallParity
{
    /// <summary>
    /// Recovers the implied European call price from an observed put price, spot,
    /// strike, and pre-computed discount factors via put-call parity.
    /// </summary>
    /// <param name="putPrice">Observed European put price.</param>
    /// <param name="spot">Current underlying spot price.</param>
    /// <param name="strike">Option strike price.</param>
    /// <param name="discountFactor">Risk-free discount factor exp(-r*T) to expiry.</param>
    /// <param name="dividendDiscountFactor">
    /// Dividend discount factor exp(-q*T). Defaults to 1 (no dividends).
    /// </param>
    /// <returns>
    /// The call price implied by put-call parity:
    /// <c>P + S * exp(-q*T) - K * exp(-r*T)</c>.
    /// </returns>
    public static decimal ImpliedCallPrice(decimal putPrice, decimal spot, decimal strike, double discountFactor, double dividendDiscountFactor = 1d)
    {
        double result = (double)putPrice + ((double)spot * dividendDiscountFactor) - ((double)strike * discountFactor);
        return Convert.ToDecimal(result);
    }

    /// <summary>
    /// Recovers the implied European put price from an observed call price, spot,
    /// strike, and pre-computed discount factors via put-call parity.
    /// </summary>
    /// <param name="callPrice">Observed European call price.</param>
    /// <param name="spot">Current underlying spot price.</param>
    /// <param name="strike">Option strike price.</param>
    /// <param name="discountFactor">Risk-free discount factor exp(-r*T) to expiry.</param>
    /// <param name="dividendDiscountFactor">
    /// Dividend discount factor exp(-q*T). Defaults to 1 (no dividends).
    /// </param>
    /// <returns>
    /// The put price implied by put-call parity:
    /// <c>C - S * exp(-q*T) + K * exp(-r*T)</c>.
    /// </returns>
    public static decimal ImpliedPutPrice(decimal callPrice, decimal spot, decimal strike, double discountFactor, double dividendDiscountFactor = 1d)
    {
        double result = (double)callPrice - ((double)spot * dividendDiscountFactor) + ((double)strike * discountFactor);
        return Convert.ToDecimal(result);
    }

    /// <summary>
    /// Recovers the implied European call price by extracting discount factors from
    /// the market curves at the given expiry date, then applying put-call parity.
    /// </summary>
    /// <param name="putPrice">Observed European put price.</param>
    /// <param name="spot">Current underlying spot price.</param>
    /// <param name="strike">Option strike price.</param>
    /// <param name="market">Market snapshot supplying discount and dividend curves.</param>
    /// <param name="expiryDate">Option expiry date used to read curve discount factors.</param>
    /// <returns>
    /// The call price implied by put-call parity using curve-derived discount factors.
    /// </returns>
    public static decimal ImpliedCallPrice(decimal putPrice, decimal spot, decimal strike, OptionMarketData market, DateOnly expiryDate)
    {
        return ImpliedCallPrice(putPrice, spot, strike, market.DiscountFactor(expiryDate), market.DividendDiscountFactor(expiryDate));
    }

    /// <summary>
    /// Recovers the implied European put price by extracting discount factors from
    /// the market curves at the given expiry date, then applying put-call parity.
    /// </summary>
    /// <param name="callPrice">Observed European call price.</param>
    /// <param name="spot">Current underlying spot price.</param>
    /// <param name="strike">Option strike price.</param>
    /// <param name="market">Market snapshot supplying discount and dividend curves.</param>
    /// <param name="expiryDate">Option expiry date used to read curve discount factors.</param>
    /// <returns>
    /// The put price implied by put-call parity using curve-derived discount factors.
    /// </returns>
    public static decimal ImpliedPutPrice(decimal callPrice, decimal spot, decimal strike, OptionMarketData market, DateOnly expiryDate)
    {
        return ImpliedPutPrice(callPrice, spot, strike, market.DiscountFactor(expiryDate), market.DividendDiscountFactor(expiryDate));
    }

    /// <summary>
    /// Tests whether an observed call/put price pair approximately satisfies put-call
    /// parity within the specified absolute tolerance.
    /// </summary>
    /// <param name="callPrice">Observed European call price.</param>
    /// <param name="putPrice">Observed European put price.</param>
    /// <param name="spot">Current underlying spot price.</param>
    /// <param name="strike">Option strike price.</param>
    /// <param name="discountFactor">Risk-free discount factor exp(-r*T) to expiry.</param>
    /// <param name="tolerance">
    /// Maximum absolute deviation allowed between the observed call and the parity-implied call.
    /// </param>
    /// <param name="dividendDiscountFactor">
    /// Dividend discount factor exp(-q*T). Defaults to 1 (no dividends).
    /// </param>
    /// <returns>
    /// <see langword="true"/> if <c>|C_observed - C_implied| &lt;= tolerance</c>;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public static bool IsApproximatelySatisfied(decimal callPrice, decimal putPrice, decimal spot, decimal strike, double discountFactor, decimal tolerance, double dividendDiscountFactor = 1d)
    {
        decimal impliedCall = ImpliedCallPrice(putPrice, spot, strike, discountFactor, dividendDiscountFactor);
        return Math.Abs(impliedCall - callPrice) <= tolerance;
    }
}
