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
/// Specifies the direction from which the underlying price must approach the barrier
/// level for the barrier event to trigger.
/// </summary>
public enum BarrierDirection
{
    /// <summary>
    /// An up-barrier: the barrier event triggers when the underlying price rises to
    /// reach or exceed the barrier level from below.
    /// </summary>
    Up = 1,

    /// <summary>
    /// A down-barrier: the barrier event triggers when the underlying price falls to
    /// reach or drop below the barrier level from above.
    /// </summary>
    Down = 2
}
