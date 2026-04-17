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
/// Specifies whether a barrier option activates or deactivates when the underlying
/// price breaches the barrier level.
/// </summary>
public enum BarrierKnockType
{
    /// <summary>
    /// A knock-in barrier: the option becomes active (starts to exist) only when the
    /// underlying price touches or crosses the barrier level during the option's life.
    /// </summary>
    In = 1,

    /// <summary>
    /// A knock-out barrier: the option ceases to exist (is extinguished) when the
    /// underlying price touches or crosses the barrier level during the option's life.
    /// </summary>
    Out = 2
}
