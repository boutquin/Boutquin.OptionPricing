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

namespace Boutquin.OptionPricing.Domain.Results;

/// <summary>
/// Describes the assumptions, limitations, and supported instrument types for a
/// specific pricing model, enabling consumers to validate that a model is appropriate
/// for their use case.
/// </summary>
/// <param name="ModelName">
/// The canonical name of the pricing model (for example "BlackScholes",
/// "CoxRossRubinstein").
/// </param>
/// <param name="Assumptions">
/// The key modeling assumptions (for example "constant volatility",
/// "continuously compounded risk-free rate", "no transaction costs").
/// </param>
/// <param name="Limitations">
/// Known limitations or conditions under which the model may produce inaccurate results
/// (for example "does not support American exercise", "assumes flat volatility smile").
/// </param>
/// <param name="SupportedInstruments">
/// The instrument types this model can price (for example "EuropeanOption",
/// "AmericanOption").
/// </param>
public sealed record ModelAssumptionSet(
    string ModelName,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> SupportedInstruments);
