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
/// <param name="Status">Convergence status of the calibration attempt.</param>
/// <param name="Diagnostics">Model-produced diagnostic entries keyed by descriptive name (e.g. step count, slice count, failure reason).</param>
public sealed record CalibrationReport(
    string ModelName,
    CalculationStatus Status,
    IReadOnlyDictionary<string, object?> Diagnostics) : ICalculationResult, IDiagnosticSource
{
    private readonly IReadOnlyDictionary<string, object?> _diagnostics = CopyDiagnostics(Diagnostics);

    /// <summary>A snapshot of named calculation diagnostics; payload objects retain their own value semantics.</summary>
    public IReadOnlyDictionary<string, object?> Diagnostics
    {
        get => _diagnostics;
        init => _diagnostics = CopyDiagnostics(value);
    }

    private static IReadOnlyDictionary<string, object?> CopyDiagnostics(IReadOnlyDictionary<string, object?> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(
            value.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    /// <summary>
    /// <see langword="true"/> when calibration converged within the requested tolerance,
    /// possibly with non-fatal warnings.
    /// </summary>
    public bool IsSuccess =>
        Status is CalculationStatus.Success or CalculationStatus.ConvergedWithWarnings;

    /// <summary>Returns the named diagnostics accompanying this calculation outcome.</summary>
    /// <returns>A read-only diagnostic snapshot; payload objects retain their own value semantics.</returns>
    public IReadOnlyDictionary<string, object?> GetDiagnostics() => Diagnostics;
}
