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

namespace Boutquin.OptionPricing.Calibration.ImpliedTrees;

/// <summary>
/// Outcome of an implied tree calibration run, identifying the model that was invoked,
/// the convergence status, and any diagnostic entries produced during calibration.
/// Implements both <see cref="ICalculationResult"/> and <see cref="IDiagnosticSource"/>
/// so consumers can inspect success/failure and internal solver state uniformly.
/// </summary>
/// <param name="ModelName">Identifier of the calibration model that produced this report (e.g. "ImpliedBinomialTreeCalibrator").</param>
/// <param name="Status">Convergence or scaffold status of the calibration attempt.</param>
/// <param name="Diagnostics">Model-produced diagnostic entries keyed by descriptive name (e.g. step count, slice count, failure reason).</param>
public sealed record CalibrationReport(
    string ModelName,
    CalculationStatus Status,
    IReadOnlyDictionary<string, object?> Diagnostics) : ICalculationResult, IDiagnosticSource
{
    /// <summary>
    /// <see langword="true"/> when calibration converged within the requested tolerance,
    /// possibly with non-fatal warnings.
    /// </summary>
    public bool IsSuccess =>
        Status is CalculationStatus.Success or CalculationStatus.ConvergedWithWarnings;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> GetDiagnostics() => Diagnostics;
}
