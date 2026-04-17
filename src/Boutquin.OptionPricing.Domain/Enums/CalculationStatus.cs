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

namespace Boutquin.OptionPricing.Domain.Enums;

/// <summary>
/// Reports the outcome of a pricing, Greeks, calibration, or root-finding calculation,
/// indicating whether the result values are trustworthy and actionable.
/// </summary>
public enum CalculationStatus
{
    /// <summary>
    /// The calculation completed successfully and all result values are valid.
    /// </summary>
    Success = 1,

    /// <summary>
    /// The calculation converged but produced non-fatal warnings (for example, residual
    /// slightly above target tolerance). Result values are usable but consumers should
    /// inspect diagnostics for details.
    /// </summary>
    ConvergedWithWarnings = 2,

    /// <summary>
    /// The iterative solver did not converge within the allowed iterations or tolerances.
    /// Result values should not be used for downstream pricing decisions; callers should
    /// widen bounds, adjust initial guesses, or increase the iteration limit.
    /// </summary>
    FailedToConverge = 3,

    /// <summary>
    /// One or more inputs failed validation (for example, non-positive spot, non-positive
    /// volatility, or degenerate interval). No computation was attempted.
    /// </summary>
    InvalidInput = 4,

    /// <summary>
    /// The requested scenario is not supported by this implementation (for example, an
    /// American option passed to a European-only pricer, or a scaffold type that has not
    /// yet been implemented). No computation was attempted.
    /// </summary>
    UnsupportedScenario = 5
}
