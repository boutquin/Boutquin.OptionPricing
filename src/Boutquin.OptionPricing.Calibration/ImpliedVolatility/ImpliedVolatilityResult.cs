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
using Boutquin.OptionPricing.Domain.Enums;

namespace Boutquin.OptionPricing.Calibration.ImpliedVolatility;

/// <summary>
/// Outcome of an implied volatility root-finding pass, carrying the recovered volatility
/// (when convergence succeeded), the solver status, iteration count, and any diagnostic
/// entries produced during the search.
/// </summary>
/// <param name="ImpliedVolatility">
/// The implied volatility value recovered by the solver, expressed as an annualized decimal
/// (e.g. 0.25 for 25 %). <see langword="null"/> when the solver did not converge or the
/// scenario is unsupported.
/// </param>
/// <param name="Status">Convergence outcome reported by the root-finding algorithm.</param>
/// <param name="Iterations">Number of root-finding iterations consumed before the solver stopped.</param>
/// <param name="Diagnostics">Solver-produced diagnostic entries keyed by descriptive name (e.g. bracket bounds, residual).</param>
public sealed record ImpliedVolatilityResult(
    double? ImpliedVolatility,
    CalculationStatus Status,
    int Iterations,
    IReadOnlyDictionary<string, object?> Diagnostics) : ICalculationResult, IDiagnosticSource
{
    /// <summary>
    /// <see langword="true"/> when the solver converged to a root within the requested
    /// convergence tolerance, possibly with non-fatal warnings.
    /// </summary>
    public bool IsSuccess =>
        Status is CalculationStatus.Success or CalculationStatus.ConvergedWithWarnings;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> GetDiagnostics() => Diagnostics;
}
