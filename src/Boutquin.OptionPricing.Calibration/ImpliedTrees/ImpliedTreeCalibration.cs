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

using Boutquin.OptionPricing.Domain.Enums;
using Boutquin.OptionPricing.Trees.Binomial;

namespace Boutquin.OptionPricing.Calibration.ImpliedTrees;

/// <summary>
/// Shared input validation for the implied-tree calibrators (F07/F08): both require a
/// positive step count, a non-null market, and a non-null surface carrying at least one slice.
/// </summary>
internal static class ImpliedTreeCalibration
{
    /// <summary>
    /// Above this many no-arbitrage node reversions the calibration is reported as
    /// <see cref="CalculationStatus.ConvergedWithWarnings"/> rather than
    /// <see cref="CalculationStatus.Success"/>. A handful of deep-wing reversions are routine
    /// for any finite Derman-Kani tree; a large count signals the tree degraded substantially
    /// toward flat constant-volatility spacing and the smile content is no longer trustworthy.
    /// </summary>
    internal const int ReversionWarningThreshold = 16;

    /// <summary>
    /// Maps the engine's no-arbitrage reversion tally to a calibration status: clean
    /// <see cref="CalculationStatus.Success"/> at or below
    /// <see cref="ReversionWarningThreshold"/>, otherwise
    /// <see cref="CalculationStatus.ConvergedWithWarnings"/> (still
    /// <see cref="CalibrationReport.IsSuccess"/> == <see langword="true"/>, so the calibrated
    /// tree remains usable but consumers should inspect the <c>nodeReversions</c> diagnostic).
    /// </summary>
    /// <param name="reversionCount">Number of nodes that reverted to the constant-volatility seed.</param>
    /// <returns>The calibration status reflecting the reversion count.</returns>
    internal static CalculationStatus StatusFromReversions(int reversionCount)
    {
        return reversionCount > ReversionWarningThreshold
            ? CalculationStatus.ConvergedWithWarnings
            : CalculationStatus.Success;
    }

    internal static void ValidateInput(ImpliedVolatilityTreeCalibrationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.Steps);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(input.Steps, TreeBuilderLimits.MaxSteps);
        ArgumentNullException.ThrowIfNull(input.Market);
        ArgumentNullException.ThrowIfNull(input.Surface);

        if (input.Surface.Slices.Count == 0)
        {
            throw new ArgumentException(
                "The volatility surface must contain at least one smile slice to calibrate an implied tree.",
                nameof(input));
        }
    }
}
