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
/// Defines the contract for calibrating a pricing model to observed market data,
/// producing fitted parameters or a calibration report.
/// </summary>
/// <typeparam name="TInput">
/// The calibration input type, typically containing observed market prices, strikes,
/// expiries, and any model-specific configuration (for example initial parameter guesses
/// or convergence tolerances).
/// </typeparam>
/// <typeparam name="TResult">
/// The calibration result type carrying fitted parameters, residual errors, convergence
/// status, and any diagnostic information.
/// </typeparam>
/// <remarks>
/// Implementations encapsulate a calibration algorithm such as implied volatility root-finding,
/// smile surface fitting, or implied binomial tree construction. The calibrator is stateless;
/// all algorithm configuration is injected at construction time.
/// </remarks>
public interface ICalibrator<in TInput, out TResult>
{
    /// <summary>
    /// Calibrates model parameters to the supplied market observations.
    /// </summary>
    /// <param name="input">
    /// The calibration input containing observed market data and solver configuration.
    /// </param>
    /// <returns>
    /// A calibration result reporting fitted parameters, convergence status, and residuals.
    /// </returns>
    TResult Calibrate(TInput input);
}
