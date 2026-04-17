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
/// Specifies whether an option contract is a call or a put,
/// determining the payoff direction at exercise.
/// </summary>
public enum CallPut
{
    /// <summary>
    /// A call option that pays max(S − K, 0) at exercise,
    /// conferring the right to buy the underlying at the strike price.
    /// </summary>
    Call = 1,

    /// <summary>
    /// A put option that pays max(K − S, 0) at exercise,
    /// conferring the right to sell the underlying at the strike price.
    /// </summary>
    Put = 2
}
