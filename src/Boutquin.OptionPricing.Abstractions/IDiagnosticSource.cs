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
/// Defines the contract for exposing intermediate diagnostic values produced during
/// a pricing or calibration calculation.
/// </summary>
/// <remarks>
/// Implementations attach named diagnostic entries such as Black-Scholes d1/d2 values,
/// discount factors, implied rates, or iteration counts to a calculation result. Consumers
/// use these diagnostics for model validation, debugging, and audit trails without
/// coupling to internal model state.
/// </remarks>
public interface IDiagnosticSource
{
    /// <summary>
    /// Returns the diagnostic entries produced during the most recent calculation,
    /// keyed by descriptive name (for example "d1", "discountFactor", "iterationCount").
    /// </summary>
    /// <returns>
    /// A read-only dictionary mapping diagnostic names to their values. The dictionary
    /// is empty when no diagnostics were recorded.
    /// </returns>
    IReadOnlyDictionary<string, object?> GetDiagnostics();
}
