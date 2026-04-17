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

using Boutquin.OptionPricing.Domain.Contracts;
using Boutquin.OptionPricing.Domain.Market;

namespace Boutquin.OptionPricing.Calibration.ImpliedVolatility;

/// <summary>
/// Encapsulates the inputs required by the implied volatility root-finding solver:
/// the option contract, its market environment, the observed market price to match,
/// and the numerical parameters that govern convergence behavior.
/// </summary>
/// <param name="Option">The option contract whose implied volatility is being recovered.</param>
/// <param name="Market">Market data snapshot (spot, rates, dividend yield) used to reprice the option at each trial volatility.</param>
/// <param name="ObservedPrice">The market-observed option premium that the solver attempts to reproduce.</param>
/// <param name="InitialGuess">Starting volatility estimate supplied to the root-finding algorithm.</param>
/// <param name="FunctionTolerance">Maximum absolute residual |model − market| for convergence (default 1e-8).</param>
/// <param name="BracketTolerance">Maximum bracket width for convergence (default 1e-8).</param>
public sealed record ImpliedVolatilityRequest(
    OptionContract Option,
    OptionMarketData Market,
    decimal ObservedPrice,
    double InitialGuess,
    double FunctionTolerance = 1e-8,
    double BracketTolerance = 1e-8);
