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
/// Specifies when the holder may exercise an option contract, selecting the
/// pricing model path that applies.
/// </summary>
public enum ExerciseStyle
{
    /// <summary>
    /// Exercise is permitted only at the expiry date. Supported by Black-Scholes
    /// closed-form and binomial tree models.
    /// </summary>
    European = 1,

    /// <summary>
    /// Exercise is permitted at any time up to and including the expiry date.
    /// Requires a tree or numerical method that evaluates early-exercise at each node;
    /// not supported by the Black-Scholes closed-form pricer.
    /// </summary>
    American = 2
}
