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

namespace Boutquin.OptionPricing.Calibration.Smile;

/// <summary>
/// Outcome of a no-arbitrage constraint check against a smile slice or volatility surface.
/// When the quotes satisfy all checked constraints, <paramref name="IsValid"/> is
/// <see langword="true"/> and <paramref name="Findings"/> is empty. Otherwise each
/// detected violation is described as a human-readable finding string.
/// </summary>
/// <param name="IsValid"><see langword="true"/> when no arbitrage violations were detected in the checked quotes.</param>
/// <param name="Findings">Descriptions of each detected no-arbitrage constraint violation (empty when the quotes are clean).</param>
public sealed record ArbitrageCheckResult(
    bool IsValid,
    IReadOnlyList<string> Findings) : ICalculationResult
{
    /// <inheritdoc />
    public bool IsSuccess => IsValid;
}
