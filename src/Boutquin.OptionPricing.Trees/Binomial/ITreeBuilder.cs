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
/// Contract for constructing a recombining binomial tree from model inputs.
/// Implementations choose the lattice parameterization (e.g., CRR up/down factors)
/// and populate the spot lattice used by downstream pricers for backward induction.
/// </summary>
public interface ITreeBuilder
{
    /// <summary>
    /// Constructs a fully populated <see cref="BinomialTree"/> whose spot lattice,
    /// up factor, down factor, and risk-neutral probability are derived from the
    /// supplied model inputs.
    /// </summary>
    /// <param name="input">
    /// Market and contract parameters — spot, strike, volatility, discount curve,
    /// time step count, and optional dividend/borrow curves.
    /// </param>
    /// <returns>
    /// A <see cref="BinomialTree"/> ready for pricing via backward induction.
    /// </returns>
    BinomialTree Build(TreeModelInput input);
}
