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

namespace Boutquin.OptionPricing.Domain.Results;

/// <summary>
/// Holds the set of option sensitivity measures (Greeks) produced by a pricing model,
/// with <see langword="null"/> indicating that a particular Greek was not computed.
/// </summary>
/// <param name="Delta">
/// Rate of change of option price with respect to a one-unit change in the
/// underlying spot price (∂V/∂S). Ranges from −1 to +1 for vanilla options.
/// </param>
/// <param name="Gamma">
/// Rate of change of delta with respect to a one-unit change in spot price
/// (∂²V/∂S²). Measures the curvature of the option price surface.
/// </param>
/// <param name="Vega">
/// Sensitivity of option price to a one-percentage-point change in annualized
/// volatility (∂V/∂σ), scaled so that a 0.01 volatility move produces the
/// reported change in price.
/// </param>
/// <param name="Theta">
/// Sensitivity of option price to the passage of one calendar day (∂V/∂t),
/// typically negative for long option positions reflecting time-value decay.
/// </param>
/// <param name="Rho">
/// Sensitivity of option price to a one-percentage-point change in the risk-free
/// interest rate (∂V/∂r).
/// </param>
public sealed record GreekSet(
    double? Delta = null,
    double? Gamma = null,
    double? Vega = null,
    double? Theta = null,
    double? Rho = null)
{
    /// <summary>
    /// A shared empty instance where all Greeks are <see langword="null"/>,
    /// representing a result with no computed sensitivities.
    /// </summary>
    public static GreekSet Empty { get; } = new();
}
