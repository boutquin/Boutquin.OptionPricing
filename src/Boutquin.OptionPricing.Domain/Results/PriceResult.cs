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

namespace Boutquin.OptionPricing.Domain.Results;

/// <summary>
/// Carries the output of an option pricing calculation: the model price, optional Greeks,
/// a status code, and diagnostic entries useful for validation and debugging.
/// </summary>
/// <param name="Price">
/// The computed option fair value, in the same currency units as the spot and strike.
/// Zero when the status indicates failure or unsupported scenario.
/// </param>
/// <param name="Greeks">
/// Optional sensitivity measures computed alongside the price.
/// <see langword="null"/> when Greeks were not requested or the calculation failed.
/// </param>
/// <param name="Status">
/// The outcome of the calculation; defaults to <see cref="CalculationStatus.Success"/>.
/// </param>
/// <param name="Diagnostics">
/// Optional dictionary of named intermediate values (for example d1, d2, discount factor)
/// produced by the pricing model for audit and debugging.
/// </param>
/// <param name="ModelName">
/// Optional name of the pricing model that produced this result (for example
/// "BlackScholes", "CRR-BinomialTree").
/// </param>
/// <param name="AssumptionSummary">
/// Optional human-readable summary of the model assumptions applied.
/// </param>
public sealed record PriceResult(
    decimal Price,
    GreekSet? Greeks = null,
    CalculationStatus Status = CalculationStatus.Success,
    IReadOnlyDictionary<string, object?>? Diagnostics = null,
    string? ModelName = null,
    string? AssumptionSummary = null) : ICalculationResult, IDiagnosticSource
{
    /// <inheritdoc />
    /// <remarks>
    /// Returns <see langword="true"/> when the status is
    /// <see cref="CalculationStatus.Success"/> or
    /// <see cref="CalculationStatus.ConvergedWithWarnings"/>.
    /// </remarks>
    public bool IsSuccess =>
        Status is CalculationStatus.Success or CalculationStatus.ConvergedWithWarnings;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> GetDiagnostics()
    {
        return Diagnostics ?? EmptyDiagnostics.Instance;
    }

    /// <summary>
    /// Creates a <see cref="PriceResult"/> with zero price and
    /// <see cref="CalculationStatus.UnsupportedScenario"/> status, recording the model
    /// name and reason for non-support in diagnostics.
    /// </summary>
    /// <param name="modelName">The pricing model that rejected the scenario.</param>
    /// <param name="reason">A human-readable explanation of why the scenario is unsupported.</param>
    /// <returns>A failed price result with diagnostic reason.</returns>
    public static PriceResult Unsupported(string modelName, string reason)
    {
        return new PriceResult(
            0m,
            null,
            CalculationStatus.UnsupportedScenario,
            new Dictionary<string, object?> { ["reason"] = reason },
            modelName);
    }

    private sealed class EmptyDiagnostics : IReadOnlyDictionary<string, object?>
    {
        public static EmptyDiagnostics Instance { get; } = new();

        public IEnumerable<string> Keys => Array.Empty<string>();

        public IEnumerable<object?> Values => Array.Empty<object?>();

        public int Count => 0;

        public object? this[string key] => null;

        public bool ContainsKey(string key) => false;

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            return Enumerable.Empty<KeyValuePair<string, object?>>().GetEnumerator();
        }

        public bool TryGetValue(string key, out object? value)
        {
            value = null;
            return false;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
