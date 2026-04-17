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
/// Extensible binomial tree builder that supports multiple lattice parameterizations
/// (CRR, Jarrow-Rudd). Delegates to the selected builder at runtime.
/// </summary>
/// <remarks>
/// The default parameterization is <see cref="TreeParameterization.CoxRossRubinstein"/>,
/// preserving backward compatibility with existing callers that construct the builder
/// without arguments.
/// </remarks>
public sealed class FlexibleBinomialTreeBuilder : ITreeBuilder
{
    private readonly ITreeBuilder _inner;

    /// <summary>
    /// Initializes a new instance using the specified lattice parameterization.
    /// </summary>
    /// <param name="parameterization">
    /// The lattice construction algorithm. Defaults to
    /// <see cref="TreeParameterization.CoxRossRubinstein"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="parameterization"/> is not a recognized value.
    /// </exception>
    public FlexibleBinomialTreeBuilder(TreeParameterization parameterization = TreeParameterization.CoxRossRubinstein)
    {
        _inner = parameterization switch
        {
            TreeParameterization.CoxRossRubinstein => new CoxRossRubinsteinTreeBuilder(),
            TreeParameterization.JarrowRudd => new JarrowRuddTreeBuilder(),
            _ => throw new ArgumentOutOfRangeException(nameof(parameterization))
        };
    }

    /// <summary>
    /// Constructs a binomial tree from the supplied model inputs using the selected
    /// parameterization.
    /// </summary>
    /// <param name="input">
    /// Market and contract parameters forwarded to the underlying tree builder.
    /// </param>
    /// <returns>A fully populated <see cref="BinomialTree"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="input"/> has non-positive spot, non-positive volatility,
    /// non-positive time to expiry, or non-positive step count.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the derived risk-neutral probability falls outside the open interval (0, 1)
    /// (CRR parameterization only).
    /// </exception>
    public BinomialTree Build(TreeModelInput input)
    {
        return _inner.Build(input);
    }
}
