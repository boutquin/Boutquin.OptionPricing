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
using Boutquin.OptionPricing.Trees.Binomial;

namespace Boutquin.OptionPricing.Calibration.ImpliedTrees;

/// <summary>
/// The output of an implied tree calibration, containing the calibrated binomial tree,
/// local volatilities at each node, Arrow-Debreu state prices, and a summary report.
/// </summary>
/// <param name="Tree">The calibrated binomial tree with smile-consistent spot lattice.</param>
/// <param name="LocalVolatilities">
/// Local volatility at each (step, state) node. Indexed as
/// <c>LocalVolatilities[step][state]</c>.
/// </param>
/// <param name="ArrowDebreuPrices">
/// Arrow-Debreu state prices at each (step, state) node. Indexed as
/// <c>ArrowDebreuPrices[step][state]</c>.
/// </param>
/// <param name="Report">The calibration report with convergence status and diagnostics.</param>
public sealed record ImpliedTreeResult(
    BinomialTree Tree,
    IReadOnlyList<IReadOnlyList<double>> LocalVolatilities,
    IReadOnlyList<IReadOnlyList<double>> ArrowDebreuPrices,
    CalibrationReport Report) : ICalculationResult
{
    /// <summary>
    /// Returns <see langword="true"/> when the calibration converged successfully.
    /// </summary>
    public bool IsSuccess => Report.IsSuccess;
}
