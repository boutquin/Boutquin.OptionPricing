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
/// Marker interface for calculation results that expose a success/failure indicator,
/// enabling uniform status checking across pricing, Greeks, and calibration operations.
/// </summary>
public interface ICalculationResult
{
    /// <summary>
    /// Indicates whether the calculation completed successfully and the result values
    /// are valid for downstream use.
    /// </summary>
    /// <remarks>
    /// When <see langword="false"/>, consumers should inspect the specific result type
    /// for a detailed <c>CalculationStatus</c> code explaining the failure (for example
    /// non-convergence, unsupported exercise style, or invalid input).
    /// </remarks>
    bool IsSuccess { get; }
}
