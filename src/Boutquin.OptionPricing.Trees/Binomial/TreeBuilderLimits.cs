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

namespace Boutquin.OptionPricing.Trees.Binomial;

/// <summary>
/// Shared numeric limits enforced by the binomial tree builders.
/// </summary>
public static class TreeBuilderLimits
{
    /// <summary>
    /// Maximum number of time steps a tree builder will accept. A recombining
    /// binomial lattice is a triangular array of <c>Steps + 1</c> layers totaling
    /// <c>~Steps^2 / 2</c> doubles, so the step count must be bounded before
    /// allocation to prevent multi-gigabyte memory exhaustion (and to avoid the
    /// <c>Steps + 1</c> integer overflow at <see cref="int.MaxValue"/>). At the
    /// ceiling the lattice holds ~50 million doubles (~400&#160;MB), which is far
    /// above any convergence requirement (production step counts are typically a
    /// few hundred to a couple thousand) yet safely bounded.
    /// </summary>
    public const int MaxSteps = 10_000;
}
