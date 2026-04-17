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
/// A single node in a recombining binomial tree, identified by its position
/// in the lattice and carrying the underlying spot price at that state.
/// </summary>
/// <param name="TimeIndex">Zero-based time step index (0 = valuation date, N = expiry).</param>
/// <param name="StateIndex">Zero-based state index within the time step (0 = highest spot, increasing index = successive down moves).</param>
/// <param name="Spot">Underlying asset price at this tree node.</param>
public sealed record TreeNode(int TimeIndex, int StateIndex, double Spot);
