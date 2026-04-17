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
/// Defines the contract for computing the fair value of an option given its contract
/// specification and a market data snapshot.
/// </summary>
/// <typeparam name="TOption">
/// The option contract type specifying call/put flag, strike, expiry, and exercise style.
/// </typeparam>
/// <typeparam name="TMarket">
/// The market data type providing spot price, volatility, discount curve, and optional
/// dividend and borrow curves required by the pricing model.
/// </typeparam>
/// <typeparam name="TResult">
/// The result type carrying the computed price, calculation status, and any model diagnostics.
/// </typeparam>
/// <remarks>
/// Implementations encapsulate a specific pricing model (for example Black-Scholes,
/// binomial tree, or Monte Carlo). The pricer is stateless; all model configuration
/// such as tree depth or convergence tolerance is injected at construction time.
/// </remarks>
public interface IOptionPricer<in TOption, in TMarket, out TResult>
{
    /// <summary>
    /// Computes the model price for the specified option under the given market conditions.
    /// </summary>
    /// <param name="option">
    /// The option contract defining the payoff structure, strike, and expiry date.
    /// </param>
    /// <param name="market">
    /// The market data snapshot supplying spot price, volatility, and term-structure curves.
    /// </param>
    /// <returns>
    /// A pricing result containing the fair value, a status indicating success or failure,
    /// and optional diagnostic entries produced by the model.
    /// </returns>
    TResult Price(TOption option, TMarket market);
}
