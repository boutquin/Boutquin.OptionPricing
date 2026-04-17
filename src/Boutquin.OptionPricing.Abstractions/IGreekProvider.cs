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

namespace Boutquin.OptionPricing.Abstractions;

/// <summary>
/// Defines the contract for computing option sensitivity measures (Greeks) given
/// a contract specification and a market data snapshot.
/// </summary>
/// <typeparam name="TOption">
/// The option contract type specifying call/put flag, strike, expiry, and exercise style.
/// </typeparam>
/// <typeparam name="TMarket">
/// The market data type providing spot price, volatility, discount curve, and optional
/// dividend and borrow curves required by the Greeks calculation.
/// </typeparam>
/// <typeparam name="TResult">
/// The result type carrying computed Greeks (delta, gamma, vega, theta, rho) and
/// calculation status.
/// </typeparam>
/// <remarks>
/// Implementations may compute Greeks analytically (for example Black-Scholes closed-form)
/// or numerically (finite-difference bumps on a pricer). The provider is stateless;
/// all configuration is injected at construction time.
/// </remarks>
public interface IGreekProvider<in TOption, in TMarket, out TResult>
{
    /// <summary>
    /// Computes the sensitivity measures (Greeks) for the specified option under the
    /// given market conditions.
    /// </summary>
    /// <param name="option">
    /// The option contract defining the payoff structure, strike, and expiry date.
    /// </param>
    /// <param name="market">
    /// The market data snapshot supplying spot price, volatility, and term-structure curves.
    /// </param>
    /// <returns>
    /// A result containing the computed Greeks (delta, gamma, vega, theta, rho) and a
    /// status indicating whether the calculation succeeded.
    /// </returns>
    TResult CalculateGreeks(TOption option, TMarket market);
}
